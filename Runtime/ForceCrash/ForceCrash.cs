using System;
using UnityEngine;
using UnityEngine.Diagnostics;
using Random = UnityEngine.Random;

namespace Utility
{
    public class ForceCrash
    {
        public void WindowOrEditor()
        {
            var enumArray = Enum.GetNames(typeof(ForcedCrashCategory));
            var randomValue = Random.Range(0, enumArray.Length);
            var enumName = enumArray[randomValue];

            var randomEnum = Enum.Parse<ForcedCrashCategory>(enumName);
            Utils.ForceCrash(randomEnum);
        }

        public void Android()
        {
            // https://stackoverflow.com/questions/17511070/android-force-crash-with-uncaught-exception-in-thread
            var message = new AndroidJavaObject("java.lang.String", "This is a test crash, ignore.");
            var exception = new AndroidJavaObject("java.lang.Exception", message);

            var looperClass = new AndroidJavaClass("android.os.Looper");
            var mainLooper = looperClass.CallStatic<AndroidJavaObject>("getMainLooper");
            var mainThread = mainLooper.Call<AndroidJavaObject>("getThread");
            var exceptionHandler = mainThread.Call<AndroidJavaObject>("getUncaughtExceptionHandler");
            exceptionHandler.Call("uncaughtException", mainThread, exception);
        }
    }
}
