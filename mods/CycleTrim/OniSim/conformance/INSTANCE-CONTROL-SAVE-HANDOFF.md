# 给 OniHarness 维护 agent：CycleTrim 实例保存接口

CycleTrim 已接入测试组准备、实际 DLL/原生映射校验和单实例有界 step。
当前 `runtime/OniHarness.Runtime/InstanceControl.cs` 仅支持 pause/step/load/quit。
下一步需要真实存读档往返，重载最初的夹具文件不能代替这个检查。

请在你负责的控制器和 companion 中补齐以下接口，保留现有 Steam 与实例隔离规则：

1. 在已加载、已暂停、身份匹配的测试实例上保存一个新文件。
   使用正常 `SaveLoader.Instance.Save(path, isAutoSave: false, updateSavePointer: false)`。
   文件限定在本实例的测试目录，不覆盖源夹具、已有文件或个人存档。
2. 保存完成后回传准确路径、SHA256、保存前后原生时钟、暂停状态、PID、
   实例身份与运行 nonce，并验证继续游戏/活动存档指针没有被这次另存改变。
3. 将该原生实例刚生成的文件加入有限的运行内批准列表，再允许精确路径与
   SHA256 匹配的加载。保持初始夹具的批准记录；不扩展成任意路径加载。
   失败保存、链接路径、跨实例文件、错误摘要和旧 nonce 均需拒绝。
4. 对外提供经过控制器同一把操作锁串行处理的 step/save/load-produced 命令，
   便于 CycleTrim 调用，避免独立文件客户端与其他 CLI 同时控制同一实例。
5. 增加宿主回归测试；标明 API 替身不是真实游戏证据。随后按无头、图形顺序
   做一次真实游戏验证：加载夹具 → step → 另存 → 加载新文件 → 核对保存时钟
   → step → 最终暂停，保留失败记录与原文件摘要。

CycleTrim 的输入已暂存于 `conformance/artifacts/cycletrim-group-20261021-a/`，
对应组为 `/tmp/ct4-20261021-a`；托管 DLL 为 `ea8c59a3`，原生库为 `1f56c9b2`。
该组当前只完成准备，没有四实例验收结论。请保留其他任务的现有实例与修改。
