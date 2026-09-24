using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IRSpeedyVPN.Services
{
    // Windows 7+ inbox WFP APIs; no external executable, driver, or permanent firewall rule.
    internal sealed class BrowserQuicFilterSession : IDisposable
    {
        private WfpEngineHandle engine;
        internal static readonly Guid ConnectV4 = new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82");
        internal static readonly Guid ConnectV6 = new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
        internal static readonly Guid AppId = new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971");
        internal static readonly Guid Protocol = new Guid("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
        internal static readonly Guid RemotePort = new Guid("c35a604d-d22b-4e1a-91b4-68f674ee674b");

        public BrowserQuicFilterSession()
        {
            var session = new Session
            {
                Display = new DisplayData { Name = "IRSpeedy Proxifier browser QUIC" },
                Flags = 1, // FWPM_SESSION_FLAG_DYNAMIC: cleanup on close AND process termination.
                TransactionTimeout = 2000
            };
            Check(FwpmEngineOpen0(null, 10 /* RPC_C_AUTHN_WINNT */, IntPtr.Zero, ref session, out engine));
        }

        public void AddBrowser(string path)
        {
            // Defense in depth: an empty/wildcard/core path must never become a system-wide rule.
            if (!BrowserExecutableDiscovery.IsBrowser(path) || !System.IO.Path.IsPathRooted(path))
                throw new ArgumentException("An absolute browser executable path is required.", nameof(path));
            if (engine == null || engine.IsClosed) throw new ObjectDisposedException(nameof(BrowserQuicFilterSession));
            IntPtr appId = IntPtr.Zero;
            IntPtr conditions = IntPtr.Zero;
            bool transaction = false;
            try
            {
                Check(FwpmGetAppIdFromFileName0(path, out appId));
                var values = CreateConditions(appId);
                int stride = Marshal.SizeOf(typeof(FilterCondition));
                conditions = Marshal.AllocHGlobal(stride * values.Length);
                for (int i = 0; i < values.Length; i++)
                    Marshal.StructureToPtr(values[i], IntPtr.Add(conditions, stride * i), false);

                Check(FwpmTransactionBegin0(engine, 0));
                transaction = true;
                foreach (Guid layer in new[] { ConnectV4, ConnectV6 })
                {
                    var filter = CreateFilter(layer, conditions);
                    ulong id;
                    Check(FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out id));
                }
                Check(FwpmTransactionCommit0(engine));
                transaction = false;
            }
            finally
            {
                if (transaction) FwpmTransactionAbort0(engine);
                if (conditions != IntPtr.Zero) Marshal.FreeHGlobal(conditions);
                if (appId != IntPtr.Zero) FwpmFreeMemory0(ref appId);
            }
        }

        internal static FilterCondition[] CreateConditions(IntPtr appId)
        {
            if (appId == IntPtr.Zero) throw new ArgumentException("Missing browser app id.", nameof(appId));
            // FWP_MATCH_EQUAL = 0; values/ports are in HOST order, not network order.
            return new[]
            {
                new FilterCondition { Field = AppId, Value = new Value { Type = 12, Data = appId } },
                new FilterCondition { Field = Protocol, Value = new Value { Type = 1, Data = new IntPtr(17) } },
                new FilterCondition { Field = RemotePort, Value = new Value { Type = 2, Data = new IntPtr(443) } }
            };
        }

        internal static Filter CreateFilter(Guid layer, IntPtr conditions)
        {
            return new Filter
            {
                Display = new DisplayData { Name = "IRSpeedy Proxifier browser UDP/443" },
                Layer = layer,
                // Empty sublayer selects the default sublayer; empty weight is automatic.
                ConditionCount = 3,
                Conditions = conditions,
                Action = new FilterAction { Type = 0x1001 } // FWP_ACTION_BLOCK
            };
        }

        public void Dispose()
        {
            engine?.Dispose();
            engine = null;
        }

        private static void Check(uint status)
        {
            if (status != 0) throw new Win32Exception(unchecked((int)status), "WFP error 0x" + status.ToString("X8"));
        }

        // Layouts mirror fwpmtypes.h/fwptypes.h with native pointer alignment on both x86/x64.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayData
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Description;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Session
        {
            public Guid Key;
            public DisplayData Display;
            public uint Flags, TransactionTimeout, ProcessId;
            public IntPtr Sid, Username;
            public int KernelMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Value
        {
            public uint Type;
            // The native union holds a pointer or <=32-bit scalar (64-bit values are pointers).
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FilterCondition
        {
            public Guid Field;
            public uint Match;
            public Value Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Blob { public uint Size; public IntPtr Data; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FilterAction { public uint Type; public Guid Key; }

        [StructLayout(LayoutKind.Explicit)]
        internal struct Context
        {
            [FieldOffset(0)] public ulong Raw;
            [FieldOffset(0)] public Guid Provider;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Filter
        {
            public Guid Key;
            public DisplayData Display;
            public uint Flags;
            public IntPtr Provider;
            public Blob ProviderData;
            public Guid Layer, SubLayer;
            public Value Weight;
            public uint ConditionCount;
            public IntPtr Conditions;
            public FilterAction Action;
            public Context Context;
            public IntPtr Reserved;
            public ulong Id;
            public Value EffectiveWeight;
        }

        internal sealed class WfpEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public WfpEngineHandle() : base(true) { }
            protected override bool ReleaseHandle() { return FwpmEngineClose0(handle) == 0; }
        }

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint FwpmEngineOpen0(string server, uint authn, IntPtr identity, ref Session session, out WfpEngineHandle engine);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmEngineClose0(IntPtr engine);
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint FwpmGetAppIdFromFileName0(string filename, out IntPtr appId);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern void FwpmFreeMemory0(ref IntPtr memory);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)]
        private static extern uint FwpmFilterAdd0(WfpEngineHandle engine, ref Filter filter, IntPtr security, out ulong id);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmTransactionBegin0(WfpEngineHandle engine, uint flags);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmTransactionCommit0(WfpEngineHandle engine);
        [DllImport("fwpuclnt.dll", ExactSpelling = true)] private static extern uint FwpmTransactionAbort0(WfpEngineHandle engine);
    }
}
