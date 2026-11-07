//! Safe, process-scoped access to the game's versioned Steam flat API.
//! All FFI signatures and result layouts follow Steamworks SDK 1.53a.

use anyhow::{Context, Result, bail, ensure};
use libloading::Library;
use serde::{Deserialize, Serialize};
use std::ffi::{CString, c_char, c_void};
use std::mem::{ManuallyDrop, MaybeUninit};
use std::path::Path;
use std::time::{Duration, Instant};

use super::abi::{self, CONSUMER_APP, CREATOR_APP, Interface};
use super::package::{Plan, hash};

// SAFETY: Each invocation supplies the exact SDK flat signature. Interfaces
// come from matching versioned factories; argument buffers outlive the call.
macro_rules! call {
    ($self:expr, $name:ident, $signature:ty $(, $argument:expr)* $(,)?) => {{
        // SAFETY: The symbol and signature are audited together at this call site.
        let symbol = unsafe { $self.library.get::<$signature>(concat!(stringify!($name), "\0").as_bytes()) }
            .with_context(|| format!("游戏 Steam SDK 缺少 {}", stringify!($name)))?;
        // SAFETY: Factory versions, buffers and handles satisfy the SDK ABI.
        unsafe { symbol($($argument),*) }
    }};
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub(super) struct Item {
    pub id: u64,
    pub creator: u32,
    pub consumer: u32,
    pub owner: u64,
    pub title: String,
    pub description: String,
    pub visibility: i32,
    pub kind: i32,
    pub banned: bool,
    pub file: u64,
    pub bytes: u64,
    pub updated: u32,
    pub tags: Vec<String>,
    pub children: Vec<u64>,
}

impl Item {
    pub(super) fn require_identity(&self, id: u64, owner: u64) -> Result<()> {
        ensure!(
            self.id == id
                && self.owner == owner
                && self.creator == CREATOR_APP
                && self.consumer == CONSUMER_APP,
            "工坊条目的 ID、所有者或 App 不匹配：{}",
            self.id
        );
        ensure!(
            !self.banned && self.kind == 0,
            "条目被封禁或不是普通 Mod：{}",
            self.id
        );
        ensure!(
            self.file != 0 && self.file != u64::MAX && self.bytes > 0,
            "条目没有可用的 Legacy ZIP，拒绝内容更新：{}",
            self.id
        );
        Ok(())
    }
}

pub(super) struct Steam {
    // Steam starts native threads. Workers exit without unloading the library;
    // unloading a live Steam runtime or shutting down after UGC download can hang.
    library: ManuallyDrop<Library>,
    remote: Interface,
    utils: Interface,
    ugc: Interface,
    pub owner: u64,
    app: u32,
}

impl Steam {
    pub(super) fn connect(path: &Path, app: u32, owner: u64) -> Result<Self> {
        for key in ["SteamAppId", "SteamGameId"] {
            ensure!(
                std::env::var(key).ok().as_deref() == Some(app.to_string().as_str()),
                "Steam worker 必须在启动前设置 {key}={app}"
            );
        }
        // SAFETY: Only the installed game's Steam library is loaded, in a
        // dedicated worker. It remains mapped until the worker exits.
        let library =
            ManuallyDrop::new(unsafe { Library::new(path) }.context("加载游戏 Steam API 失败")?);
        let mut api = Self {
            library,
            remote: std::ptr::null_mut(),
            utils: std::ptr::null_mut(),
            ugc: std::ptr::null_mut(),
            owner: 0,
            app,
        };
        ensure!(
            call!(api, SteamAPI_IsSteamRunning, unsafe extern "C" fn() -> bool),
            "Steam 客户端未运行"
        );
        ensure!(
            call!(api, SteamAPI_Init, unsafe extern "C" fn() -> bool),
            "Steam 初始化失败，请先登录 Steam"
        );
        api.utils = call!(
            api,
            SteamAPI_SteamUtils_v010,
            unsafe extern "C" fn() -> Interface
        );
        api.remote = call!(
            api,
            SteamAPI_SteamRemoteStorage_v016,
            unsafe extern "C" fn() -> Interface
        );
        let user = call!(
            api,
            SteamAPI_SteamUser_v021,
            unsafe extern "C" fn() -> Interface
        );
        api.ugc = call!(
            api,
            SteamAPI_SteamUGC_v016,
            unsafe extern "C" fn() -> Interface
        );
        ensure!(
            !api.utils.is_null() && !api.remote.is_null() && !user.is_null() && !api.ugc.is_null(),
            "Steam SDK 接口不可用"
        );
        let actual = call!(
            api,
            SteamAPI_ISteamUtils_GetAppID,
            unsafe extern "C" fn(Interface) -> u32,
            api.utils
        );
        ensure!(actual == app, "Steam App 上下文错误：{actual}，应为 {app}");
        ensure!(
            call!(
                api,
                SteamAPI_ISteamUser_BLoggedOn,
                unsafe extern "C" fn(Interface) -> bool,
                user
            ),
            "Steam 账号尚未登录"
        );
        api.owner = call!(
            api,
            SteamAPI_ISteamUser_GetSteamID,
            unsafe extern "C" fn(Interface) -> u64,
            user
        );
        ensure!(
            owner != 0 && owner == api.owner,
            "Steam 已登录账号 {} 与预期 {owner} 不同",
            api.owner
        );
        println!("steamContextApp={app}; steamOwner={owner}");
        Ok(api)
    }

    fn wait<T: Copy>(&self, handle: u64, callback: i32, timeout: Duration) -> Result<T> {
        ensure!(
            handle != 0 && handle != u64::MAX,
            "Steam 返回无效调用句柄；callback={callback}"
        );
        let deadline = Instant::now() + timeout;
        loop {
            call!(self, SteamAPI_RunCallbacks, unsafe extern "C" fn());
            let mut failed = 0_u8;
            if call!(
                self,
                SteamAPI_ISteamUtils_IsAPICallCompleted,
                unsafe extern "C" fn(Interface, u64, *mut u8) -> bool,
                self.utils,
                handle,
                &mut failed
            ) {
                ensure!(failed == 0, "Steam 调用 IO 失败；callback={callback}");
                let mut result = MaybeUninit::<T>::uninit();
                let bytes = i32::try_from(std::mem::size_of::<T>())?;
                ensure!(
                    call!(
                        self,
                        SteamAPI_ISteamUtils_GetAPICallResult,
                        unsafe extern "C" fn(
                            Interface,
                            u64,
                            *mut c_void,
                            i32,
                            i32,
                            *mut u8,
                        ) -> bool,
                        self.utils,
                        handle,
                        result.as_mut_ptr().cast(),
                        bytes,
                        callback,
                        &mut failed
                    ) && failed == 0,
                    "Steam 回调尺寸、类型或结果不匹配；callback={callback}"
                );
                // SAFETY: This private method is called only with the matching
                // SDK callback records. The SDK checked the callback ID and size;
                // every field is an integer, float or byte array, valid for all bits.
                return Ok(unsafe { result.assume_init() });
            }
            ensure!(
                Instant::now() < deadline,
                "Steam 回调超时；callback={callback}；写入状态可能不确定，禁止重复请求"
            );
            std::thread::sleep(Duration::from_millis(40));
        }
    }

    pub(super) fn details(&self, id: u64) -> Result<Item> {
        let call = call!(
            self,
            SteamAPI_ISteamRemoteStorage_GetPublishedFileDetails,
            unsafe extern "C" fn(Interface, u64, u32) -> u64,
            self.remote,
            id,
            0
        );
        let d: abi::Details = self.wait(call, 1318, Duration::from_secs(60))?;
        ensure!(
            d.result == 1 && d.id == id && d.file_bytes > 0,
            "读取 Legacy 条目失败：{id}"
        );
        ensure!(d.tags_truncated == 0, "条目标签被截断，拒绝覆盖");
        Ok(Item {
            id: d.id,
            creator: d.creator,
            consumer: d.consumer,
            owner: d.owner,
            title: text(&d.title)?,
            description: text(&d.description)?,
            visibility: d.visibility,
            kind: d.kind,
            banned: d.banned != 0,
            file: d.file,
            bytes: d.file_bytes as u64,
            updated: d.updated,
            tags: tags(&d.tags)?,
            children: Vec::new(),
        })
    }

    pub(super) fn owned(&self) -> Result<Vec<Item>> {
        ensure!(
            self.app == CREATOR_APP,
            "枚举上传器条目必须使用 Creator 上下文"
        );
        let mut items = Vec::new();
        let mut offset = 0_u32;
        for _ in 0..10 {
            let call = call!(
                self,
                SteamAPI_ISteamRemoteStorage_EnumerateUserPublishedFiles,
                unsafe extern "C" fn(Interface, u32) -> u64,
                self.remote,
                offset
            );
            let page: abi::Owned = self.wait(call, 1312, Duration::from_secs(60))?;
            ensure!(
                page.result == 1 && (0..=50).contains(&page.returned) && page.total >= 0,
                "枚举上传器条目失败"
            );
            let ids = page.ids;
            for id in &ids[..page.returned as usize] {
                let item = self.details(*id)?;
                ensure!(item.owner == self.owner, "枚举结果包含其他账号的条目");
                items.push(item);
            }
            offset += page.returned as u32;
            if page.returned == 0 || offset >= page.total as u32 {
                return Ok(items);
            }
        }
        bail!("条目枚举超过 500 项预算")
    }

    pub(super) fn share(&self, name: &str, bytes: &[u8]) -> Result<()> {
        ensure!(self.app == CREATOR_APP, "上传文件必须使用 Creator 上下文");
        let name = CString::new(name)?;
        let mut total = 0_u64;
        let mut available = 0_u64;
        ensure!(
            call!(
                self,
                SteamAPI_ISteamRemoteStorage_GetQuota,
                unsafe extern "C" fn(Interface, *mut u64, *mut u64) -> bool,
                self.remote,
                &mut total,
                &mut available
            ) && available >= bytes.len() as u64,
            "Steam Cloud 空间不足或无法读取配额"
        );
        ensure!(
            call!(
                self,
                SteamAPI_ISteamRemoteStorage_FileWrite,
                unsafe extern "C" fn(Interface, *const c_char, *const c_void, i32) -> bool,
                self.remote,
                name.as_ptr(),
                bytes.as_ptr().cast(),
                i32::try_from(bytes.len())?
            ),
            "Steam Cloud 文件写入失败"
        );
        let call = call!(
            self,
            SteamAPI_ISteamRemoteStorage_FileShare,
            unsafe extern "C" fn(Interface, *const c_char) -> u64,
            self.remote,
            name.as_ptr()
        );
        let result: abi::Share = self.wait(call, 1307, Duration::from_secs(180))?;
        ensure!(
            result.result == 1 && result.handle != 0 && result.handle != u64::MAX,
            "Steam Cloud FileShare 失败"
        );
        Ok(())
    }

    pub(super) fn upload(
        &self,
        plan: &Plan,
        zip_name: &str,
        preview_name: &str,
        current: Option<&Item>,
    ) -> Result<u64> {
        ensure!(self.app == CREATOR_APP, "内容发布必须使用 Creator 上下文");
        let zip = CString::new(zip_name)?;
        let preview = CString::new(preview_name)?;
        let title = CString::new(plan.title.as_str())?;
        let description = CString::new(plan.english.as_str())?;
        let note = CString::new(plan.change_note.as_str())?;
        let tag_values: Vec<_> = current
            .map(|item| item.tags.iter().map(String::as_str).collect())
            .unwrap_or_else(|| vec!["New Features", "Base Game"])
            .into_iter()
            .map(CString::new)
            .collect::<std::result::Result<_, _>>()?;
        let mut pointers: Vec<_> = tag_values.iter().map(|tag| tag.as_ptr()).collect();
        let mut tags = abi::Strings {
            strings: pointers.as_mut_ptr(),
            count: i32::try_from(pointers.len())?,
        };
        let (call, callback) = if plan.target_id == 0 {
            (
                call!(
                    self,
                    SteamAPI_ISteamRemoteStorage_PublishWorkshopFile,
                    unsafe extern "C" fn(
                        Interface,
                        *const c_char,
                        *const c_char,
                        u32,
                        *const c_char,
                        *const c_char,
                        i32,
                        *mut abi::Strings,
                        i32,
                    ) -> u64,
                    self.remote,
                    zip.as_ptr(),
                    preview.as_ptr(),
                    CONSUMER_APP,
                    title.as_ptr(),
                    description.as_ptr(),
                    2,
                    &mut tags,
                    0
                ),
                1309,
            )
        } else {
            let update = call!(
                self,
                SteamAPI_ISteamRemoteStorage_CreatePublishedFileUpdateRequest,
                unsafe extern "C" fn(Interface, u64) -> u64,
                self.remote,
                plan.target_id
            );
            ensure!(
                update != 0 && update != u64::MAX,
                "创建 Legacy 更新句柄失败"
            );
            for (label, value) in [
                (
                    "file",
                    call!(
                        self,
                        SteamAPI_ISteamRemoteStorage_UpdatePublishedFileFile,
                        unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                        self.remote,
                        update,
                        zip.as_ptr()
                    ),
                ),
                (
                    "preview",
                    call!(
                        self,
                        SteamAPI_ISteamRemoteStorage_UpdatePublishedFilePreviewFile,
                        unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                        self.remote,
                        update,
                        preview.as_ptr()
                    ),
                ),
                (
                    "title",
                    call!(
                        self,
                        SteamAPI_ISteamRemoteStorage_UpdatePublishedFileTitle,
                        unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                        self.remote,
                        update,
                        title.as_ptr()
                    ),
                ),
                (
                    "description",
                    call!(
                        self,
                        SteamAPI_ISteamRemoteStorage_UpdatePublishedFileDescription,
                        unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                        self.remote,
                        update,
                        description.as_ptr()
                    ),
                ),
                (
                    "change note",
                    call!(
                        self,
                        SteamAPI_ISteamRemoteStorage_UpdatePublishedFileSetChangeDescription,
                        unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                        self.remote,
                        update,
                        note.as_ptr()
                    ),
                ),
                (
                    "tags",
                    call!(
                        self,
                        SteamAPI_ISteamRemoteStorage_UpdatePublishedFileTags,
                        unsafe extern "C" fn(Interface, u64, *mut abi::Strings) -> bool,
                        self.remote,
                        update,
                        &mut tags
                    ),
                ),
            ] {
                ensure!(value, "设置 Legacy {label} 失败");
            }
            (
                call!(
                    self,
                    SteamAPI_ISteamRemoteStorage_CommitPublishedFileUpdate,
                    unsafe extern "C" fn(Interface, u64) -> u64,
                    self.remote,
                    update
                ),
                1316,
            )
        };
        let result: abi::Published = self.wait(call, callback, Duration::from_secs(1200))?;
        let id = result.id;
        println!(
            "workshopCallbackId={id}; result={}; needsAgreement={}",
            result.result, result.needs_agreement
        );
        ensure!(
            result.result == 1 && result.needs_agreement == 0 && id != 0 && id != u64::MAX,
            "工坊写入未得到可用成功结果；不要重复创建，检查持久发布日志"
        );
        ensure!(
            plan.target_id == 0 || id == plan.target_id,
            "Steam 更新回调返回了不同条目 ID"
        );
        Ok(id)
    }

    pub(super) fn query(&self, id: u64, language: &str) -> Result<Item> {
        ensure!(
            self.app == CONSUMER_APP,
            "UGC 查询必须使用游戏 Consumer 上下文"
        );
        let language = CString::new(language)?;
        let query = call!(
            self,
            SteamAPI_ISteamUGC_CreateQueryUGCDetailsRequest,
            unsafe extern "C" fn(Interface, *const u64, u32) -> u64,
            self.ugc,
            &id,
            1
        );
        ensure!(query != 0 && query != u64::MAX, "UGC 查询句柄无效");
        let outcome = (|| -> Result<Item> {
            ensure!(
                call!(
                    self,
                    SteamAPI_ISteamUGC_SetAllowCachedResponse,
                    unsafe extern "C" fn(Interface, u64, u32) -> bool,
                    self.ugc,
                    query,
                    0
                ),
                "UGC 禁用缓存失败"
            );
            ensure!(
                call!(
                    self,
                    SteamAPI_ISteamUGC_SetLanguage,
                    unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                    self.ugc,
                    query,
                    language.as_ptr()
                ),
                "UGC 设置语言失败"
            );
            ensure!(
                call!(
                    self,
                    SteamAPI_ISteamUGC_SetReturnLongDescription,
                    unsafe extern "C" fn(Interface, u64, bool) -> bool,
                    self.ugc,
                    query,
                    true
                ),
                "UGC 长说明查询失败"
            );
            ensure!(
                call!(
                    self,
                    SteamAPI_ISteamUGC_SetReturnChildren,
                    unsafe extern "C" fn(Interface, u64, bool) -> bool,
                    self.ugc,
                    query,
                    true
                ),
                "UGC 前置查询失败"
            );
            let call = call!(
                self,
                SteamAPI_ISteamUGC_SendQueryUGCRequest,
                unsafe extern "C" fn(Interface, u64) -> u64,
                self.ugc,
                query
            );
            let result: abi::Queried = self.wait(call, 3401, Duration::from_secs(60))?;
            ensure!(
                result.result == 1 && result.handle == query && result.returned == 1,
                "UGC 条目查询失败"
            );
            let mut details = MaybeUninit::<abi::UgcDetails>::uninit();
            ensure!(
                call!(
                    self,
                    SteamAPI_ISteamUGC_GetQueryUGCResult,
                    unsafe extern "C" fn(Interface, u64, u32, *mut abi::UgcDetails) -> bool,
                    self.ugc,
                    query,
                    0,
                    details.as_mut_ptr()
                ),
                "UGC 条目读取失败"
            );
            // SAFETY: The SDK wrote a complete version-016 SteamUGCDetails_t.
            // Its layout is checked against the Valve header compiler probe.
            let d = unsafe { details.assume_init() };
            ensure!(
                d.result == 1
                    && d.id == id
                    && d.file_bytes > 0
                    && d.tags_truncated == 0
                    && d.children <= 4096,
                "UGC 身份、内容或前置数量无效"
            );
            let mut children = vec![0_u64; d.children as usize];
            if !children.is_empty() {
                ensure!(
                    call!(
                        self,
                        SteamAPI_ISteamUGC_GetQueryUGCChildren,
                        unsafe extern "C" fn(Interface, u64, u32, *mut u64, u32) -> bool,
                        self.ugc,
                        query,
                        0,
                        children.as_mut_ptr(),
                        u32::try_from(children.len())?
                    ),
                    "UGC 前置列表读取失败"
                );
            }
            Ok(Item {
                id: d.id,
                creator: d.creator,
                consumer: d.consumer,
                owner: d.owner,
                title: text(&d.title)?,
                description: text(&d.description)?,
                visibility: d.visibility,
                kind: d.kind,
                banned: d.banned != 0,
                file: d.file,
                bytes: d.file_bytes as u64,
                updated: d.updated,
                tags: tags(&d.tags)?,
                children,
            })
        })();
        call!(
            self,
            SteamAPI_ISteamUGC_ReleaseQueryUGCRequest,
            unsafe extern "C" fn(Interface, u64) -> bool,
            self.ugc,
            query
        );
        outcome
    }

    pub(super) fn verify_bytes(&self, item: &Item, plan: &Plan) -> Result<()> {
        ensure!(
            self.app == CONSUMER_APP,
            "内容下载必须从游戏 Consumer 上下文验证"
        );
        item.require_identity(item.id, plan.owner)?;
        ensure!(
            item.bytes == plan.zip_bytes && item.bytes <= 100 * 1024 * 1024,
            "工坊 ZIP 大小与候选不符"
        );
        let call = call!(
            self,
            SteamAPI_ISteamRemoteStorage_UGCDownload,
            unsafe extern "C" fn(Interface, u64, u32) -> u64,
            self.remote,
            item.file,
            0
        );
        let result: abi::Downloaded = self.wait(call, 1317, Duration::from_secs(300))?;
        ensure!(
            result.result == 1
                && result.handle == item.file
                && result.bytes > 0
                && result.bytes as u64 == plan.zip_bytes
                && result.owner == plan.owner
                && matches!(result.app, CREATOR_APP | CONSUMER_APP),
            "UGC 下载结果与条目不匹配"
        );
        let mut bytes = vec![0_u8; result.bytes as usize];
        for (index, chunk) in bytes.chunks_mut(1024 * 1024).enumerate() {
            let offset = u32::try_from(index * 1024 * 1024)?;
            let count = call!(
                self,
                SteamAPI_ISteamRemoteStorage_UGCRead,
                unsafe extern "C" fn(Interface, u64, *mut c_void, i32, u32, i32) -> i32,
                self.remote,
                item.file,
                chunk.as_mut_ptr().cast(),
                i32::try_from(chunk.len())?,
                offset,
                0
            );
            ensure!(count == i32::try_from(chunk.len())?, "UGC ZIP 读取不完整");
        }
        ensure!(
            hash(&bytes) == plan.zip_sha256,
            "实际 UGC 下载 ZIP 的 SHA256 与候选不符"
        );
        println!(
            "verifiedDownloadedZipSha256={}; item={}",
            plan.zip_sha256, item.id
        );
        Ok(())
    }

    pub(super) fn metadata(&self, id: u64, plan: &Plan, language: &str) -> Result<()> {
        let description = if language == "english" {
            &plan.english
        } else {
            &plan.chinese
        };
        let update = self.start_metadata(id)?;
        let language = CString::new(language)?;
        let title = CString::new(plan.title.as_str())?;
        let description = CString::new(description.as_str())?;
        ensure!(
            call!(
                self,
                SteamAPI_ISteamUGC_SetItemUpdateLanguage,
                unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                self.ugc,
                update,
                language.as_ptr()
            ),
            "设置工坊语言失败"
        );
        ensure!(
            call!(
                self,
                SteamAPI_ISteamUGC_SetItemTitle,
                unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                self.ugc,
                update,
                title.as_ptr()
            ),
            "设置工坊标题失败"
        );
        ensure!(
            call!(
                self,
                SteamAPI_ISteamUGC_SetItemDescription,
                unsafe extern "C" fn(Interface, u64, *const c_char) -> bool,
                self.ugc,
                update,
                description.as_ptr()
            ),
            "设置工坊说明失败"
        );
        self.submit_metadata(update, id)
    }

    pub(super) fn visibility(&self, id: u64, visibility: i32) -> Result<()> {
        let update = self.start_metadata(id)?;
        ensure!(
            call!(
                self,
                SteamAPI_ISteamUGC_SetItemVisibility,
                unsafe extern "C" fn(Interface, u64, i32) -> bool,
                self.ugc,
                update,
                visibility
            ),
            "设置工坊可见性失败"
        );
        self.submit_metadata(update, id)
    }

    fn start_metadata(&self, id: u64) -> Result<u64> {
        ensure!(
            self.app == CONSUMER_APP,
            "元数据修改必须使用 Consumer 上下文"
        );
        let update = call!(
            self,
            SteamAPI_ISteamUGC_StartItemUpdate,
            unsafe extern "C" fn(Interface, u32, u64) -> u64,
            self.ugc,
            CONSUMER_APP,
            id
        );
        ensure!(update != 0 && update != u64::MAX, "元数据更新句柄无效");
        Ok(update)
    }

    fn submit_metadata(&self, update: u64, id: u64) -> Result<()> {
        let call = call!(
            self,
            SteamAPI_ISteamUGC_SubmitItemUpdate,
            unsafe extern "C" fn(Interface, u64, *const c_char) -> u64,
            self.ugc,
            update,
            std::ptr::null()
        );
        let result: abi::Submitted = self.wait(call, 3404, Duration::from_secs(180))?;
        ensure!(
            result.result == 1 && result.id == id && result.needs_agreement == 0,
            "元数据提交未得到可用成功回调"
        );
        Ok(())
    }

    pub(super) fn add_dependency(&self, parent: u64, child: u64) -> Result<()> {
        ensure!(self.app == CONSUMER_APP, "前置关系必须使用 Consumer 上下文");
        let call = call!(
            self,
            SteamAPI_ISteamUGC_AddDependency,
            unsafe extern "C" fn(Interface, u64, u64) -> u64,
            self.ugc,
            parent,
            child
        );
        let result: abi::Dependency = self.wait(call, 3412, Duration::from_secs(60))?;
        ensure!(
            result.result == 1 && result.parent == parent && result.child == child,
            "工坊前置回调失败或 ID 不符"
        );
        Ok(())
    }
}

fn text(bytes: &[u8]) -> Result<String> {
    let length = bytes
        .iter()
        .position(|b| *b == 0)
        .context("Steam 字符串没有终止符")?;
    Ok(std::str::from_utf8(&bytes[..length])
        .context("Steam 返回非 UTF-8 文本")?
        .to_owned())
}

fn tags(bytes: &[u8]) -> Result<Vec<String>> {
    Ok(text(bytes)?
        .split(',')
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .map(str::to_owned)
        .collect())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn foreign_strings_require_bounded_termination_and_utf8() {
        assert_eq!(text(b"hello\0tail").unwrap(), "hello");
        assert!(text(b"unterminated").is_err());
        assert!(text(&[0xff, 0]).is_err());
    }
}
