using Android.App;
using Android.Runtime;
using System;

namespace MFAAvalonia.Android;

internal static class AndroidPythonServiceInterop
{
    public static void Prepare(Activity activity, string serviceClass) =>
        CallWithContext(activity, serviceClass, "prepare", "(Landroid/content/Context;)V");

    public static void Start(Activity activity, string serviceClass, string argument)
    {
        var classHandle = FindClass(serviceClass);
        var method = JNIEnv.GetStaticMethodID(
            classHandle,
            "start",
            "(Landroid/content/Context;Ljava/lang/String;)V");
        using var javaArgument = new Java.Lang.String(argument);
        JNIEnv.CallStaticVoidMethod(classHandle, method,
        [
            new JValue(activity),
            new JValue(javaArgument),
        ]);
    }

    public static void Stop(Activity activity, string serviceClass) =>
        CallWithContext(activity, serviceClass, "stop", "(Landroid/content/Context;)V");

    private static void CallWithContext(Activity activity, string serviceClass, string methodName, string signature)
    {
        var classHandle = FindClass(serviceClass);
        var method = JNIEnv.GetStaticMethodID(classHandle, methodName, signature);
        JNIEnv.CallStaticVoidMethod(classHandle, method, [new JValue(activity)]);
    }

    private static IntPtr FindClass(string serviceClass)
    {
        var jniName = serviceClass.Replace('.', '/');
        var classHandle = JNIEnv.FindClass(jniName);
        if (classHandle == IntPtr.Zero)
            throw new InvalidOperationException($"python-for-android service class was not found: {serviceClass}");
        return classHandle;
    }
}
