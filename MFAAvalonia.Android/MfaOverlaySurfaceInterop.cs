using Android.Runtime;
using Android.Views;

namespace MFAAvalonia.Android;

internal static class MfaOverlaySurfaceInterop
{
    private const string ClassName = "com/fox/MFAAvalonia/MfaOverlaySurface";

    internal static bool TryExcludeFromScreenshots(View view)
    {
        var classHandle = JNIEnv.FindClass(ClassName);
        try
        {
            var method = JNIEnv.GetStaticMethodID(classHandle, "excludeFromScreenshots",
                "(Landroid/view/View;)Z");
            return JNIEnv.CallStaticBooleanMethod(classHandle, method,
                new JValue(view));
        }
        finally
        {
            JNIEnv.DeleteLocalRef(classHandle);
        }
    }
}
