"""Check production TCP/TLS probes using local TLS and CONNECT fixtures.
Requires .NET 8 and openssl; trusts only generated fixture certificates in the child process.
"""
from pathlib import Path
import os
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
harness = r'''
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRSpeedyVPN.Common;
namespace IRSpeedyVPN.Common {
 internal static class LogHelper { internal static void WriteExLog(string text) { } }
}
class Program {
 static int passed;
 static void Check(bool condition,string name) {
  if(!condition) throw new Exception(name);
  Console.WriteLine("PASS "+name);passed++;
 }
 static async Task Main(string[] args) {
  using var trusted = new X509Certificate2(args[0], "");
  using var untrusted = new X509Certificate2(args[1], "");
  await Probe(trusted,true,"tls-probe.invalid",true,"CONNECT tunnel uses remote hostname and TLS, without an HTTP page request");
  await Probe(trusted,false,"localhost",true,"system-tunnel mode performs TCP and verified TLS");
  await Probe(trusted,true,"wrong-probe.invalid",false,"certificate hostname mismatch is rejected");
  await Probe(untrusted,true,"tls-probe.invalid",false,"untrusted certificate is rejected");
  await ProxyFailure("HTTP/1.1 502 Bad Gateway\r\n\r\n",false,"rejected CONNECT fails");
  await ProxyFailure("not-http 200 OK\r\n\r\n",false,"malformed CONNECT status fails");
  await ProxyFailure(new string('X',8192),false,"oversized CONNECT header fails");
  await ProxyFailure(null,false,"stalled proxy is bounded by the overall deadline");
  await ProxyFailure(null,true,"caller cancellation interrupts a stalled proxy");
  // A permissive legacy HTTP callback must not weaken SslStream validation.
  ServicePointManager.ServerCertificateValidationCallback = (_,_,_,_) => true;
  await Probe(untrusted,true,"tls-probe.invalid",false,"global HTTP certificate override cannot bypass TLS validation");
  ServicePointManager.ServerCertificateValidationCallback = null;
  Console.WriteLine(passed+" TCP/TLS checks passed.");
 }
 static async Task<string> ReadHeader(NetworkStream stream) {
  var text=new StringBuilder();var b=new byte[1];
  while(text.Length<8192) {
   if(await stream.ReadAsync(b,0,1)==0) throw new IOException();
   text.Append((char)b[0]);
   if(text.ToString().EndsWith("\r\n\r\n")) return text.ToString();
  }
  throw new IOException();
 }
 static async Task Probe(X509Certificate2 cert,bool proxy,string host,bool expected,string name) {
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
  int port=((IPEndPoint)listener.LocalEndpoint).Port;
  // Direct mode has a fixed destination port 443. Bind that port only for this case.
  if(!proxy) {listener.Stop();listener=new TcpListener(IPAddress.Loopback,443);listener.Start();}
  string header=null;int applicationBytes=0;
  var server=Task.Run(async()=> {
   using var connection=await listener.AcceptTcpClientAsync();
   var stream=connection.GetStream();
   if(proxy) {
    header=await ReadHeader(stream);
    byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
    await stream.WriteAsync(response);
   }
   using var tls=new SslStream(stream,false);
   try {
    await tls.AuthenticateAsServerAsync(cert,false,SslProtocols.Tls12,false);
    applicationBytes=await tls.ReadAsync(new byte[1]);
   } catch(AuthenticationException) {} catch(IOException) {}
  });
  try {
   bool result=await ConnectionTlsTest.CheckAsync(host,3000,proxy ? port : (int?)null,CancellationToken.None);
   await server.WaitAsync(TimeSpan.FromSeconds(5));
   Check(result==expected && applicationBytes==0 && (!proxy || header.StartsWith("CONNECT "+host+":443 HTTP/1.1\r\n")),name);
  } finally {listener.Stop();}
 }
 static async Task ProxyFailure(string response,bool cancel,string name) {
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
  int port=((IPEndPoint)listener.LocalEndpoint).Port;
  using var request=new CancellationTokenSource();
  var server=Task.Run(async()=> {
   using var c=await listener.AcceptTcpClientAsync();
   await ReadHeader(c.GetStream());
   if(cancel) request.Cancel();
   if(response!=null) await c.GetStream().WriteAsync(Encoding.ASCII.GetBytes(response));
   else await Task.Delay(350);
  });
  try {
   bool cancelled=false,result=false;
   try {result=await ConnectionTlsTest.CheckAsync("tls-probe.invalid",200,port,request.Token);}
   catch(OperationCanceledException) {cancelled=true;}
   await server.WaitAsync(TimeSpan.FromSeconds(5));
   Check(!result && cancelled==cancel,name);
  } finally {listener.Stop();}
 }
}
'''
with tempfile.TemporaryDirectory(prefix='connection-tls-checks-') as directory:
    path = Path(directory)
    for name in ('trusted', 'untrusted'):
        subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes',
                        '-keyout', str(path / (name+'.key')), '-out', str(path / (name+'.pem')),
                        '-days', '1', '-subj', '/CN=tls-probe.invalid',
                        '-addext', 'subjectAltName=DNS:tls-probe.invalid,DNS:localhost',
                        '-addext', 'basicConstraints=critical,CA:TRUE'], check=True, capture_output=True)
        subprocess.run(['openssl', 'pkcs12', '-export', '-inkey', str(path/(name+'.key')),
                        '-in', str(path/(name+'.pem')), '-out', str(path/(name+'.pfx')),
                        '-passout', 'pass:'], check=True, capture_output=True)
    (path/'Program.cs').write_text(harness)
    (path/'ConnectionTlsTest.cs').write_text((root/'IRSpeedyVPN/Common/ConnectionTlsTest.cs').read_text())
    (path/'checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><NoWarn>SYSLIB0014;SYSLIB0057</NoWarn></PropertyGroup></Project>')
    env = dict(os.environ, SSL_CERT_FILE=str(path/'trusted.pem'), SSL_CERT_DIR=str(path/'empty-certs'))
    (path/'empty-certs').mkdir()
    subprocess.run([sys.argv[1] if len(sys.argv)>1 else 'dotnet', 'run', '--project', directory,
                    '-v:q', '--', str(path/'trusted.pfx'), str(path/'untrusted.pfx')], check=True, env=env)
