using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class RobloxHandleCloser
{
    // Windows API imports
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryObject(
        IntPtr ObjectHandle,
        int ObjectInformationClass,
        IntPtr ObjectInformation,
        int ObjectInformationLength,
        out int ReturnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtDuplicateObject(
        IntPtr SourceProcessHandle,
        IntPtr SourceHandle,
        IntPtr TargetProcessHandle,
        out IntPtr TargetHandle,
        int DesiredAccess,
        int Attributes,
        int Options);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(
        int dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    // Constants
    private const int SystemExtendedHandleInformation = 64;
    private const int ObjectNameInformation = 1;
    private const int PROCESS_DUP_HANDLE = 0x0040;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int DUPLICATE_CLOSE_SOURCE = 0x00000001;
    private const int DUPLICATE_SAME_ACCESS = 0x00000002;

    // Structures for 64-bit systems
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_INFORMATION_EX
    {
        public IntPtr NumberOfHandles;
        public IntPtr Reserved;
        // Followed by SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX array
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Roblox Handle Closer - Started");
        Console.WriteLine("Monitoring for RobloxPlayerBeta.exe processes...");
        Console.WriteLine("Target handle: *\\BaseNamedObjects\\ROBLOX_singletonEvent (any session)");
        Console.WriteLine("Press Ctrl+C to exit\n");

        while (true)
        {
            try
            {
                DateTime checkTime = DateTime.Now;
                Console.WriteLine(string.Format("[{0:HH:mm:ss}] Checking for processes...", checkTime));
                
                Process[] robloxProcesses = Process.GetProcessesByName("RobloxPlayerBeta");
                
                if (robloxProcesses.Length == 0)
                {
                    Console.WriteLine(string.Format("[{0:HH:mm:ss}] No RobloxPlayerBeta.exe processes found.", checkTime));
                }
                else
                {
                    Console.WriteLine(string.Format("[{0:HH:mm:ss}] Found {1} RobloxPlayerBeta.exe process(es).", checkTime, robloxProcesses.Length));
                    
                    foreach (Process process in robloxProcesses)
                    {
                        Console.WriteLine(string.Format("  - PID {0}: Scanning handles...", process.Id));
                        CloseTargetHandle(process.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error: {0}", ex.Message));
            }

            // Wait for 60 seconds before next check
            Thread.Sleep(15000);
        }
    }

    static void CloseTargetHandle(int processId)
    {
        const string targetHandleSuffix = "\\BaseNamedObjects\\ROBLOX_singletonEvent";
        
        try
        {
            IntPtr processHandle = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_INFORMATION, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                Console.WriteLine(string.Format("    Failed to open process {0} - may need Administrator rights", processId));
                return;
            }

            try
            {
                // Get system handle information with larger initial buffer
                int length = 0x200000; // Start with 2MB
                IntPtr ptr = IntPtr.Zero;
                int returnLength;
                int result;

                do
                {
                    ptr = Marshal.AllocHGlobal(length);
                    result = NtQuerySystemInformation(SystemExtendedHandleInformation, ptr, length, out returnLength);
                    
                    if (result == 0) // STATUS_SUCCESS
                        break;
                    
                    Marshal.FreeHGlobal(ptr);
                    ptr = IntPtr.Zero;
                    
                    if (result == unchecked((int)0xC0000004)) // STATUS_INFO_LENGTH_MISMATCH
                    {
                        length = returnLength + 0x10000;
                    }
                    else
                    {
                        Console.WriteLine(string.Format("    NtQuerySystemInformation failed with status: 0x{0:X8}", result));
                        return;
                    }
                } while (length < 0x4000000); // Max 64MB

                if (ptr == IntPtr.Zero)
                {
                    Console.WriteLine("    Failed to query system handles");
                    return;
                }

                try
                {
                    // Read the SYSTEM_HANDLE_INFORMATION_EX structure
                    SYSTEM_HANDLE_INFORMATION_EX handleInfo = (SYSTEM_HANDLE_INFORMATION_EX)Marshal.PtrToStructure(
                        ptr, typeof(SYSTEM_HANDLE_INFORMATION_EX));
                    
                    long handleCount = handleInfo.NumberOfHandles.ToInt64();
                    
                    // Skip past the header to get to the array
                    int headerSize = Marshal.SizeOf(typeof(SYSTEM_HANDLE_INFORMATION_EX));
                    IntPtr handlePtr = IntPtr.Add(ptr, headerSize);
                    
                    int handleEntrySize = Marshal.SizeOf(typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX));
                    
                    int foundCount = 0;
                    int scannedCount = 0;
                    int processHandleCount = 0;
                    int dupFailCount = 0;
                    int firstDupError = 0;
                    
                    for (long i = 0; i < handleCount; i++)
                    {
                        IntPtr currentHandlePtr = IntPtr.Add(handlePtr, (int)(i * handleEntrySize));
                        SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX handleEntry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX)Marshal.PtrToStructure(
                            currentHandlePtr, typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX));
                        
                        int entryProcessId = handleEntry.UniqueProcessId.ToInt32();
                        
                        if (entryProcessId != processId)
                            continue;

                        processHandleCount++;
                        
                        // Try to duplicate the handle with DUPLICATE_SAME_ACCESS
                        IntPtr duplicatedHandle;
                        int dupResult = NtDuplicateObject(
                            processHandle,
                            handleEntry.HandleValue,
                            GetCurrentProcess(),
                            out duplicatedHandle,
                            0,
                            0,
                            DUPLICATE_SAME_ACCESS);

                        if (dupResult != 0)
                        {
                            dupFailCount++;
                            if (firstDupError == 0)
                                firstDupError = dupResult;
                            continue;
                        }

                        scannedCount++;

                        try
                        {
                            string handleName = GetHandleName(duplicatedHandle);
                            
                            if (handleName != null)
                            {
                                if (handleName.EndsWith(targetHandleSuffix, StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.WriteLine(string.Format("    Found target handle: {0}", handleName));
                                    Console.WriteLine(string.Format("    Closing handle 0x{0:X}...", handleEntry.HandleValue.ToInt32()));
                                    
                                    // Close the handle in the target process
                                    IntPtr dummy;
                                    int closeResult = NtDuplicateObject(
                                        processHandle,
                                        handleEntry.HandleValue,
                                        IntPtr.Zero,
                                        out dummy,
                                        0,
                                        0,
                                        DUPLICATE_CLOSE_SOURCE);

                                    if (closeResult == 0)
                                    {
                                        Console.WriteLine("    Successfully closed handle!");
                                        foundCount++;
                                    }
                                    else
                                    {
                                        Console.WriteLine(string.Format("    Failed to close handle. Status: 0x{0:X8}", closeResult));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(string.Format("    Handle 0x{0:X} - Exception: {1}", handleEntry.HandleValue.ToInt32(), ex.Message));
                        }
                        finally
                        {
                            CloseHandle(duplicatedHandle);
                        }
                    }
                    
                    if (foundCount > 0)
                    {
                        Console.WriteLine(string.Format("    Closed {0} target handle(s)", foundCount));
                    }
                    else
                    {
                        Console.WriteLine("    No target handles found");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(string.Format("    Error processing handles: {0}", ex.Message));
        }
    }

    static string GetHandleName(IntPtr handle)
    {
        try
        {
            int length = 0x2000;
            IntPtr ptr = Marshal.AllocHGlobal(length);
            
            try
            {
                int returnLength;
                int result = NtQueryObject(handle, ObjectNameInformation, ptr, length, out returnLength);
                
                if (result != 0)
                {
                    Marshal.FreeHGlobal(ptr);
                    
                    if (returnLength > 0 && returnLength < 0x100000)
                    {
                        ptr = Marshal.AllocHGlobal(returnLength);
                        length = returnLength;
                        result = NtQueryObject(handle, ObjectNameInformation, ptr, length, out returnLength);
                    }
                    
                    if (result != 0)
                        return null;
                }

                UNICODE_STRING nameInfo = (UNICODE_STRING)Marshal.PtrToStructure(ptr, typeof(UNICODE_STRING));
                
                if (nameInfo.Length == 0 || nameInfo.Buffer == IntPtr.Zero)
                    return null;

                string name = Marshal.PtrToStringUni(nameInfo.Buffer, nameInfo.Length / 2);
                return name;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return null;
        }
    }
}