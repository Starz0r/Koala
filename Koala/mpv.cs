using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Koala
{
    public class Mpv
    {
        #region Definitions
        public delegate IntPtr MyGetProcAddress(IntPtr context, string name);
        public delegate void MyOpenGLCallbackUpdate(IntPtr context);
        #endregion Definitions

        #region Methods
        public Mpv()
        {
        }

        public IntPtr Create()
        {
            return mpv_create();
        }

        public MpvErrorCode Initalize(IntPtr mpv_handle)
        {
            return (MpvErrorCode)mpv_initialize(mpv_handle);
        }

        public MpvErrorCode SetOptionString(IntPtr mpv_handle, byte[] option, byte[] value)
        {
            return (MpvErrorCode)mpv_set_option_string(mpv_handle, option, value);
        }

        public IntPtr GetSubApi(IntPtr mpv_handle, int value)
        {
            return mpv_get_sub_api(mpv_handle, value);
        }

        public MpvErrorCode OpenGLCallbackInitialize(IntPtr gl_context, byte[] exts, MyGetProcAddress callback, IntPtr fn_context)
        {
            return (MpvErrorCode)mpv_opengl_cb_init_gl(gl_context, exts, callback, fn_context);

        }

        public MpvErrorCode OpenGLCallbackSetUpdate(IntPtr gl_context, MyOpenGLCallbackUpdate callback, IntPtr callback_context)
        {
            return (MpvErrorCode)mpv_opengl_cb_set_update_callback(gl_context, callback, callback_context);
        }

        public MpvErrorCode ExecuteCommand(IntPtr mpv_handle, params string[] args)
        {
            IntPtr[] byteArrayPointers;
            var mainPtr = AllocateUtf8IntPtrArrayWithSentinel(args, out byteArrayPointers);
            MpvErrorCode result = (MpvErrorCode)mpv_command(mpv_handle, mainPtr);
            foreach (var ptr in byteArrayPointers)
            {
                Marshal.FreeHGlobal(ptr);
            }
            Marshal.FreeHGlobal(mainPtr);
            return result;
        }

        public static MpvErrorCode OpenGLCallbackDraw(IntPtr context, int framebuffer_object, int width, int height)
        {
            return (MpvErrorCode)mpv_opengl_cb_draw(context, framebuffer_object, width, height);
        }

        #endregion Methods

        #region Helpers
        public static byte[] GetUtf8Bytes(String s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }

        public static IntPtr AllocateUtf8IntPtrArrayWithSentinel(string[] arr, out IntPtr[] byteArrayPointers)
        {
            int numberOfStrings = arr.Length + 1; // add extra element for extra null pointer last (sentinel)
            byteArrayPointers = new IntPtr[numberOfStrings];
            IntPtr rootPointer = Marshal.AllocCoTaskMem(IntPtr.Size * numberOfStrings);
            for (int index = 0; index < arr.Length; index++)
            {
                var bytes = GetUtf8Bytes(arr[index]);
                IntPtr unmanagedPointer = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, unmanagedPointer, bytes.Length);
                byteArrayPointers[index] = unmanagedPointer;
            }
            Marshal.Copy(byteArrayPointers, 0, rootPointer, numberOfStrings);
            return rootPointer;
        }
        #endregion Helpers

        #region Enumeration
        public enum MpvErrorCode
        {
            MPV_ERROR_SUCCESS = 0,
            MPV_ERROR_EVENT_QUEUE_FULL = -1,
            MPV_ERROR_NOMEM = -2,
            MPV_ERROR_UNINITIALIZED = -3,
            MPV_ERROR_INVALID_PARAMETER = -4,
            MPV_ERROR_OPTION_NOT_FOUND = -5,
            MPV_ERROR_OPTION_FORMAT = -6,
            MPV_ERROR_OPTION_ERROR = -7,
            MPV_ERROR_PROPERTY_NOT_FOUND = -8,
            MPV_ERROR_PROPERTY_FORMAT = -9,
            MPV_ERROR_PROPERTY_UNAVAILABLE = -10,
            MPV_ERROR_PROPERTY_ERROR = -11,
            MPV_ERROR_COMMAND = -12,
            MPV_ERROR_LOADING_FAILED = -13,
            MPV_ERROR_AO_INIT_FAILED = -14,
            MPV_ERROR_VO_INIT_FAILED = -15,
            MPV_ERROR_NOTHING_TO_PLAY = -16,
            MPV_ERROR_UNKNOWN_FORMAT = -17,
            MPV_ERROR_UNSUPPORTED = -18,
            MPV_ERROR_NOT_IMPLEMENTED = -19,
            MPV_ERROR_GENERIC = -20
        }

        public enum MpvFormat
        {
            MPV_FORMAT_NONE,
            MPV_FORMAT_STRING,
            MPV_FORMAT_OSD_STRING,
            MPV_FORMAT_FLAG,
            MPV_FORMAT_INT64,
            MPV_FORMAT_DOUBLE,
            MPV_FORMAT_NODE,
            MPV_FORMAT_NODE_ARRAY,
            MPV_FORMAT_NODE_MAP,
            MPV_FORMAT_BYTE_ARRAY
        }
        #endregion Enumeration

        #region Imports
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        public static extern IntPtr LoadPackagedLibrary([MarshalAs(UnmanagedType.LPWStr)]string libraryName, int reserved = 0);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string librayName);

        private const string libmpv = "mpv.dll";

        [DllImport(libmpv, EntryPoint = "mpv_create", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_create();

        [DllImport(libmpv, EntryPoint = "mpv_initialize", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_initialize(IntPtr mpv_handle);

        [DllImport(libmpv, EntryPoint = "mpv_set_option_string", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_option_string(IntPtr mpv_handle, byte[] option, byte[] value);

        [DllImport(libmpv, EntryPoint = "mpv_get_sub_api", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_get_sub_api(IntPtr mpv_handle, int value);

        [DllImport(libmpv, EntryPoint = "mpv_opengl_cb_init_gl", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_opengl_cb_init_gl(IntPtr gl_context, byte[] exts, MyGetProcAddress callback, IntPtr fn_context);

        [DllImport(libmpv, EntryPoint = "mpv_opengl_cb_set_update_callback", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_opengl_cb_set_update_callback(IntPtr gl_context, MyOpenGLCallbackUpdate callback, IntPtr callback_context);

        [DllImport(libmpv, EntryPoint = "mpv_command", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_command(IntPtr mpv_handle, IntPtr strings);

        [DllImport(libmpv, EntryPoint = "mpv_opengl_cb_draw", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_opengl_cb_draw(IntPtr context, int fbo, int width, int height);
        #endregion Imports
    }
}
