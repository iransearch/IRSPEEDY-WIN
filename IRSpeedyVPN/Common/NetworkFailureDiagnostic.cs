using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace IRSpeedyVPN.Common
{
    // Fixed classifications preserve the cause without exposing exception
    // messages, endpoints, URLs, certificates or authentication material.
    internal static class NetworkFailureDiagnostic
    {
        internal static string ExceptionFields(Exception exception)
        {
            string fields = "exception=" + exception.GetType().Name;
            Exception cause = exception;
            for (int i = 0; cause != null && i < 8; i++, cause = cause.InnerException)
            {
                var socket = cause as SocketException;
                if (socket != null)
                    return fields + " cause=SocketException socketError=" + socket.SocketErrorCode
                        + " nativeError=" + socket.NativeErrorCode;
                var web = cause as WebException;
                if (web != null) fields += " webStatus=" + web.Status;
                if (cause is AuthenticationException) fields += " reason=tls-authentication";
                if (cause is TimeoutException) fields += " reason=timeout";
                if (cause is InvalidOperationException && (cause.Message.StartsWith("unknown method", StringComparison.OrdinalIgnoreCase)
                    || cause.Message.StartsWith("rpc: can't find method", StringComparison.OrdinalIgnoreCase)
                    || cause.Message.StartsWith("rpc: can't find service", StringComparison.OrdinalIgnoreCase)))
                    fields += " reason=rpc-method-unavailable";
                if (cause.InnerException == null) fields += " cause=" + cause.GetType().Name;
            }
            return fields;
        }

        internal static string CoreReason(string text)
        {
            if (string.IsNullOrEmpty(text)) return "none";
            string value = text.Substring(0, Math.Min(text.Length, 4096)).ToLowerInvariant();
            if (value.Contains("forbidden by its access permissions") || value.Contains("permission denied")
                || value.Contains("access is denied")) return "socket-access-denied";
            if (value.Contains("requested address is not valid") || value.Contains("cannot assign requested address")) return "address-unavailable";
            if (value.Contains("no qualified outbound") || value.Contains("no healthy") || value.Contains("empty tag")) return "no-healthy-outbound";
            if (value.Contains("connection refused")) return "connection-refused";
            if (value.Contains("network is unreachable")) return "network-unreachable";
            if (value.Contains("no such host") || value.Contains("dns") && value.Contains("failed")) return "dns-failure";
            if (value.Contains("certificate") || value.Contains("tls handshake")) return "tls-failure";
            if (value.Contains("no recent network activity")) return "quic-idle-timeout";
            if (value.Contains("timeout") || value.Contains("deadline exceeded")) return "timeout";
            if (value.Contains("connection reset")) return "connection-reset";
            if (value.Contains("unexpected eof")) return "unexpected-eof";
            if (value.Contains("context canceled") || value.Contains("operation canceled")) return "canceled";
            return "unclassified";
        }
    }
}
