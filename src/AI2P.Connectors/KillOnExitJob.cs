using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// Объект задания Windows с флагом JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE (T-453-S0): процессы,
/// включённые в него, система снимает сама, когда закрывается последний описатель задания, —
/// то есть когда завершается сервер AI2P любым способом, в том числе аварийно. Нужен локальным
/// серверам моделей (ComfyUI, llama-server): их вывод идёт в трубу сервера, и переживший нас
/// процесс Python падает на первой же записи в неё с «[Errno 22] Invalid argument».
/// На других системах ничего не делает.
/// </summary>
internal static class KillOnExitJob
{
    private static ILogger Logger => Log.ForContext("SourceContext", nameof(KillOnExitJob));

    private static readonly Lazy<IntPtr> Job = new(Create);

    /// <summary>Включить процесс в задание; неудача не мешает работе — пишется в журнал.</summary>
    public static void Assign(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        try
        {
            var job = Job.Value;
            if (job == IntPtr.Zero || !AssignProcessToJobObject(job, process.Handle))
            {
                Logger.Warning("Не удалось привязать процесс {Pid} к объекту задания (код {Error}): " +
                               "после аварийного завершения AI2P он останется жить",
                    process.Id, Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Не удалось привязать процесс {Pid} к объекту задания", process.Id);
        }
    }

    private static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        // описатель намеренно не закрывается: его закроет система при выходе нашего процесса
        return job;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpInfo, uint cbInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
