using System;

namespace MPYakan
{
    internal class Common
    {
        // プログラムタイトル
        public static readonly string PROGRAM_TITLE = "[KMD015SC] 切削夜間バッチ";
        public static readonly string PROGRAM_NAME = "MP-Yakan";
        public static readonly string PROGRAM_VERSION = "260429.01";

        // 定義情報
        public static readonly string APP_SETTING_FILE = "appsettings.json";

        // メッセージ定義
        public static readonly int MSG_PAD = 6;
        public static readonly string MSG_SEPARATOR =
"-------------------------------------------------------------------------------";

        // エラーメッセージ定義
        public static readonly string ERR_NOT_NUMERIC = "数値を入力してください．";

        public static readonly string MSG_DATABESE_CONFIG_NOT_EXSIST = "データベース設定ファイルが存在しません\n設定ファイルを配置しアプリを再起動してください";
        public static readonly string MSG_FILE_CONFIG_NOT_EXSIST = "ファイル設定ファイルが存在しません\n設定ファイルを配置しアプリを再起動してください";
        public static readonly string MSG_DATABESE_CONNECTION_FAILURE = "データベースへの接続に失敗しました";
        public static readonly string MSG_DATABESE_CLOSE_FAILURE = "データベースへの切断に失敗しました";
        public static readonly string MSG_KM8420_REFRESH_FAILURE = "データベースの更新に失敗しました";

        public static readonly string MSG_PROGRAM_ERROR = "プログラムの想定エラーが発生しました";

    }

    public static class ParseExtensions
    {
        public static double ToDoubleSafe(this object value, double defaultValue = 0)
        {
            if (value == null || value == DBNull.Value) return defaultValue;
            if (double.TryParse(value.ToString(), out double result))
                return result;
            return defaultValue;
        }
        public static int? ToIntNullable(this object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (int.TryParse(value.ToString(), out int result))
                return result;

            return null;
        }
    }

    public static class ConsoleExtensions
    {
        /// <summary>
        /// 文字列を PAD_SIZE で PadLeft して Console.WriteLine する。
        /// </summary>
        public static void ConsoleWriteLinePadded(this string value)
        {
            Console.WriteLine(" ".PadLeft(Common.MSG_PAD) + value);
        }
    }

    public static class CompareExtensions
    {
        private const double EPS = 0.0000001;
        public static bool NearlyEquals(this double a, double b)
        {
            return Math.Abs(a - b) < EPS;
        }
        public static bool IntEquals(this int? a, int? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Value == b.Value;
        }
    }

    internal static class AssemblyState
    {
        public const bool IsDebug =
#if DEBUG
        true;
#else
        false;
#endif
    }

}
