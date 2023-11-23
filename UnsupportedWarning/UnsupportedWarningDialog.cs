using UnityEditor;

namespace Av3Emulator.UnsupportedWarning
{
    [InitializeOnLoad]
    internal static class UnsupportedWarningDialog
    {
        static UnsupportedWarningDialog() => EditorApplication.delayCall += ShowDialog;

        private static void ShowDialog()
        {
            var isJapanese = true;

            while (true)
            {
                string[] messages;
                messages = isJapanese ? jaMessage : enMessage;

                var result = EditorUtility.DisplayDialogComplex(
                    messages[0], messages[1], messages[2], messages[3], messages[4]);

                switch (result)
                {
                    case 0:
                        return;
                    case 1:
                        return;
                    case 2:
                        isJapanese = !isJapanese;
                        break;
                }
            }
        }

        private static string[] enMessage =
        {
            "Anatawa12's Fork Of Av3Emulator",
            "This fork of Av3Emulator is no longer supported.\n" +
            "Please use the original Av3Emulator from curated repository!",
            "Ok",
            "Close",
            "日本語で読む"
        };
        
        private static string[] jaMessage =
        {
            "Anatawa12's Fork Of Av3Emulator",
            "このAv3Emulatorのフォークはもうサポートされていません。\n" +
            "公式のAv3Emulatorを使ってください！",
            "OK",
            "閉じる",
            "Read in English"
        };
    }
}