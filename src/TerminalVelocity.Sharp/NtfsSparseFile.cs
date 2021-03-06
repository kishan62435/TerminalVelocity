﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Illumina.TerminalVelocity
{
    public static class SparseFile
    {
        private const int MAX_PATH = 260;
        private const uint FILE_SUPPORTS_SPARSE_FILES = 64;
        private const uint FSCTL_SET_SPARSE = ((uint)0x00000009 << 16) | ((uint)49 << 2);
        private const uint FSCTL_SET_ZERO_DATA = ((uint)0x00000009 << 16) | ((uint)50 << 2) | ((uint)2 << 14);
        private const uint GENERIC_WRITE_ACCESS = 0x40000000; // GenericWrite
        private static bool supportsSparse = true;

        public static void CreateSparse(string filename, long length)
        {
            if (!supportsSparse)
                return;

            // Ensure we have the full path
            filename = Path.GetFullPath(filename);

            try
            {
                if (!CanCreateSparse(filename))
                    return;

                // Create a file with the sparse flag enabled

                uint bytesReturned = 0;
                uint sharing = 0; // none
                uint attributes = 0x00000080; // Normal
                uint creation = 1; // Only create if new

                using (
                    SafeFileHandle handle = CreateFileW(filename, GENERIC_WRITE_ACCESS, sharing, IntPtr.Zero, creation, attributes,
                                                        IntPtr.Zero))
                {
                    // If we couldn't create the file, bail out
                    if (handle.IsInvalid)
                        return;

                    // If we can't set the sparse bit, bail out
                    if (
                        !DeviceIoControl(handle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, ref bytesReturned,
                                         IntPtr.Zero))
                        return;

                    // Tell the filesystem to mark bytes 0 -> length as sparse zeros
                    var data = new FileZeroDataInformation(0, length);
                    var structSize = (uint)Marshal.SizeOf(data);
                    IntPtr ptr = Marshal.AllocHGlobal((int)structSize);

                    try
                    {
                        Marshal.StructureToPtr(data, ptr, false);
                        DeviceIoControl(handle, FSCTL_SET_ZERO_DATA, ptr,
                                        structSize, IntPtr.Zero, 0, ref bytesReturned, IntPtr.Zero);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
            }
            catch (DllNotFoundException)
            {
                supportsSparse = false;
            }
            catch (EntryPointNotFoundException)
            {
                supportsSparse = false;
            }
            catch
            {
                // Ignore for now. Maybe if i keep hitting this i should abort future attemts
            }
        }

        private static bool CanCreateSparse(string volume)
        {
            // Ensure full path is supplied
            volume = Path.GetPathRoot(volume);

            var volumeName = new StringBuilder(MAX_PATH);
            var systemName = new StringBuilder(MAX_PATH);

            uint fsFlags, serialNumber, maxComponent;

            bool result = GetVolumeInformationW(volume, volumeName, MAX_PATH, out serialNumber, out maxComponent,
                                                out fsFlags, systemName, MAX_PATH);
            return result && (fsFlags & FILE_SUPPORTS_SPARSE_FILES) == FILE_SUPPORTS_SPARSE_FILES;
        }

        // ReSharper disable InconsistentNaming
        [DllImport("Kernel32.dll")]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr InBuffer,
            uint nInBufferSize,
            IntPtr OutBuffer,
            uint nOutBufferSize,
            ref uint pBytesReturned,
            [In] IntPtr lpOverlapped
            );

        [DllImport("kernel32.dll")]
        private static extern SafeFileHandle CreateFileW(
            [In] [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            [In] IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            [In] IntPtr hTemplateFile
            );

        [DllImport("kernel32.dll")]
        private static extern bool GetVolumeInformationW(
            [In] [MarshalAs(UnmanagedType.LPWStr)] string lpRootPathName,
            [Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            [Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder lpFileSystemNameBuffer,
            uint nFileSystemNameSize
            );
        // ReSharper restore InconsistentNaming
        [StructLayout(LayoutKind.Sequential)]
        private struct FileZeroDataInformation
        {
            public FileZeroDataInformation(long offset, long count)
            {
                FileOffset = offset;
                BeyondFinalZero = offset + count;
            }

            public readonly long FileOffset;
            public readonly long BeyondFinalZero;
        }
    }
}