use anyhow::{bail, Context, Result};
use clap::{Parser, Subcommand};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{fs, path::PathBuf, process::Command, time::{SystemTime, UNIX_EPOCH}};

const PROTOCOL: &str = "2025-11-25";

#[derive(Parser)]
#[command(name = "oni-mcp", version, about = "OniMcp HTTP client for the command line")]
struct Cli { #[command(subcommand)] command: Commands, #[arg(long, global = true)] config: Option<PathBuf> }

#[derive(Subcommand)]
enum Commands {
    /// Set the OniMcp Streamable HTTP endpoint.
    SetUrl { url: String },
    /// Store the MCP authentication secret locally.
    SetSecret { secret: String },
    /// Remove the locally stored secret.
    ClearSecret,
    /// Show endpoint and credential status (never prints the secret).
    Config,
    /// Check server reachability and MCP initialization.
    Status,
    /// List tools advertised by the running server.
    Tools,
    /// Call a tool with a JSON object of arguments.
    Call { name: String, #[arg(default_value = "{}")] arguments: String },
    /// Read an MCP resource by URI.
    Read { uri: String },
}

#[derive(Default, Serialize, Deserialize)]
struct Settings { #[serde(default = "default_url")] url: String, #[serde(default)] secret: Option<String> }
fn default_url() -> String { "http://localhost:8788/mcp/".into() }

fn config_path(explicit: Option<PathBuf>) -> Result<PathBuf> {
    if let Some(p) = explicit { return Ok(p); }
    let home = std::env::var_os("HOME").or_else(|| std::env::var_os("USERPROFILE"))
        .context("Cannot locate home directory; pass --config")?;
    Ok(PathBuf::from(home).join(".config").join("onim").join("mcp.toml"))
}
fn load(path: &PathBuf) -> Result<Settings> {
    if !path.exists() { return Ok(Settings { url: default_url(), secret: None }); }
    let s = fs::read_to_string(path).with_context(|| format!("read {}", path.display()))?;
    toml::from_str(&s).context("invalid OniMcp config")
}
fn save(path: &PathBuf, settings: &Settings) -> Result<()> {
    if let Some(parent) = path.parent() { fs::create_dir_all(parent)?; }
    let data = toml::to_string(settings)?;
    fs::write(path, data).with_context(|| format!("write {}", path.display()))?;
    #[cfg(unix)] {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(path, fs::Permissions::from_mode(0o600))
            .context("restrict config permissions to 0600")?;
    }
    Ok(())
}
fn curl(settings: &Settings, method: &str, payload: Option<&Value>, session: Option<&str>) -> Result<(u16, String, String)> {
    let nonce = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos();
    let stem = format!("oni-mcp-{}-{nonce}", std::process::id());
    let dir = std::env::temp_dir();
    let body_path = dir.join(format!("{stem}.json"));
    let header_path = dir.join(format!("{stem}.headers"));
    let output_path = dir.join(format!("{stem}.out"));
    if let Some(body) = payload { fs::write(&body_path, serde_json::to_vec(body)?)?; }
    let mut cmd = Command::new("curl");
    cmd.args(["--silent", "--show-error", "--max-time", "60", "--request", "POST",
        "--header", "Content-Type: application/json", "--header", "Accept: application/json, text/event-stream",
        "--header", &format!("Mcp-Protocol-Version: {PROTOCOL}"),
        "--dump-header"]).arg(&header_path).arg("--output").arg(&output_path);
    if let Some(secret) = std::env::var("ONIM_MCP_SECRET").ok().or_else(|| settings.secret.clone()) {
        cmd.args(["--header", &format!("Authorization: Bearer {secret}")]);
    }
    if let Some(id) = session { cmd.args(["--header", &format!("Mcp-Session-Id: {id}")]); }
    if payload.is_some() { cmd.arg("--data-binary").arg(format!("@{}", body_path.display())); }
    cmd.arg(&settings.url);
    let result = cmd.output().context("failed to execute curl; install curl and ensure it is on PATH")?;
    let _ = fs::remove_file(&body_path);
    if !result.status.success() {
        let _ = fs::remove_file(&header_path); let _ = fs::remove_file(&output_path);
        bail!("HTTP transport failed: {}", String::from_utf8_lossy(&result.stderr).trim());
    }
    let headers = fs::read_to_string(&header_path).unwrap_or_default();
    let body = fs::read_to_string(&output_path).unwrap_or_default();
    let _ = fs::remove_file(header_path); let _ = fs::remove_file(output_path);
    let status = headers.lines().find_map(|l| {
        let p: Vec<_> = l.split_whitespace().collect();
        if p.len() >= 2 && p[0].starts_with("HTTP/") { p[1].parse().ok() } else { None }
    }).unwrap_or(0);
    let sid = headers.lines().find_map(|l| {
        let (k,v)=l.split_once(':')?;
        if k.eq_ignore_ascii_case("Mcp-Session-Id") { Some(v.trim().to_string()) } else { None }
    }).unwrap_or_default();
    Ok((status, sid, body))
}
fn rpc(settings: &Settings, method: &str, params: Value, session: Option<&str>) -> Result<(Value, String)> {
    let id = SystemTime::now().duration_since(UNIX_EPOCH)?.as_millis() as u64;
    let req = json!({"jsonrpc":"2.0","id":id,"method":method,"params":params});
    let (status, sid, body) = curl(settings, method, Some(&req), session)?;
    if !(200..300).contains(&status) { bail!("server returned HTTP {status}: {body}"); }
    let parsed: Value = serde_json::from_str(&body).with_context(|| format!("invalid MCP response: {body}"))?;
    if let Some(error) = parsed.get("error") { bail!("MCP error: {error}"); }
    Ok((parsed.get("result").cloned().unwrap_or(Value::Null), if sid.is_empty() { session.unwrap_or("").to_string() } else { sid }))
}
fn request(settings: &Settings, method: &str, params: Value) -> Result<Value> {
    let (init, session) = rpc(settings, "initialize", json!({
        "protocolVersion": PROTOCOL, "capabilities": {}, "clientInfo": {"name":"oni-mcp-cli","version":env!("CARGO_PKG_VERSION")}
    }), None)?;
    if session.is_empty() { bail!("server did not return Mcp-Session-Id during initialize"); }
    let _ = curl(settings, "notifications/initialized", Some(&json!({"jsonrpc":"2.0","method":"notifications/initialized"})), Some(&session))?;
    let (result, _) = rpc(settings, method, params, Some(&session))?;
    let _ = init;
    Ok(result)
}
fn main() -> Result<()> {
    let cli=Cli::parse();
    let path=config_path(cli.config)?;
    let mut settings=load(&path)?;
    match cli.command {
        Commands::SetUrl { url } => {
            let parsed=url::Url::parse(&url).context("invalid URL")?;
            if !matches!(parsed.scheme(),"http"|"https") || parsed.host_str().is_none() { bail!("endpoint must be an absolute HTTP(S) URL"); }
            settings.url=url; save(&path,&settings)?; println!("OniMcp endpoint saved.");
        }
        Commands::SetSecret { secret } => {
            if secret.trim().is_empty() { bail!("secret cannot be empty"); }
            settings.secret=Some(secret); save(&path,&settings)?; println!("Secret saved; it will not be displayed.");
        }
        Commands::ClearSecret => { settings.secret=None; save(&path,&settings)?; println!("Stored secret removed."); }
        Commands::Config => println!("Endpoint: {}\nSecret: {}\nConfig: {}",settings.url,if settings.secret.is_some() || std::env::var_os("ONIM_MCP_SECRET").is_some() {"configured"} else {"not configured"},path.display()),
        Commands::Status => { let r=request(&settings,"ping",json!({}))?; println!("OniMcp reachable; MCP ping succeeded.\n{}",serde_json::to_string_pretty(&r)?); }
        Commands::Tools => { let r=request(&settings,"tools/list",json!({}))?; println!("{}",serde_json::to_string_pretty(&r)?); }
        Commands::Call { name, arguments } => {
            let args:Value=serde_json::from_str(&arguments).context("arguments must be a JSON object")?;
            if !args.is_object() { bail!("tool arguments must be a JSON object"); }
            let r=request(&settings,"tools/call",json!({"name":name,"arguments":args}))?;
            println!("{}",serde_json::to_string_pretty(&r)?);
        }
        Commands::Read { uri } => { let r=request(&settings,"resources/read",json!({"uri":uri}))?; println!("{}",serde_json::to_string_pretty(&r)?); }
    }
    Ok(())
}
