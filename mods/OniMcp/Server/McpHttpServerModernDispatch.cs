using System;
using System.Net;
using System.Threading;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private void DispatchModernPostResponse(HttpListenerResponse response, JsonRpcRequest rpcRequest)
        {
            if (!IsModernRequestMethod(rpcRequest.Method))
            {
                response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                SendJson(response, JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.MethodNotFound,
                    $"Method is not available on the {ModernProtocolVersion} compatibility path: {rpcRequest.Method}"),
                    (int)HttpStatusCode.NotFound);
                return;
            }

            MainThreadHttpAdmissionLease admission;
            if (!TryAcquireMainThreadHttpAdmission(response, rpcRequest.Id, null, true, out admission))
                return;

            EnqueueAdmittedMainThread(admission, new System.Action(() =>
            {
                object result = null;
                Exception processEx = null;
                try
                {
                    result = _running
                        ? ProcessModernMethod(rpcRequest)
                        : JsonRpcResponse.MakeError(rpcRequest.Id, McpErrorCode.InternalError, "MCP server is stopping");
                }
                catch (Exception ex)
                {
                    processEx = ex;
                }

                ThreadPool.QueueUserWorkItem(_ => SendModernPostResponse(response, rpcRequest.Id, result, processEx));
            }), () => CloseStaleHttpResponse(response));
        }

        private void SendModernPostResponse(HttpListenerResponse response, object requestId, object result,
            Exception processEx)
        {
            try
            {
                response.Headers["Mcp-Protocol-Version"] = ModernProtocolVersion;
                if (processEx != null)
                {
                    SendJson(response, JsonRpcResponse.MakeError(requestId, McpErrorCode.InternalError,
                        processEx.Message), 200);
                    return;
                }

                if (result is JsonRpcResponse rpcResponse)
                {
                    int status = rpcResponse.Error?.Code == McpErrorCode.MethodNotFound ? 404 : 200;
                    SendJson(response, rpcResponse, status);
                }
                else
                    SendJson(response, JsonRpcResponse.Success(requestId, result), 200);
            }
            catch (Exception ex)
            {
                OniMcp.Support.OniMcpLog.Warning($"[OniMcp] Failed to send modern MCP response: {ex.GetType().Name}: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }
    }
}
