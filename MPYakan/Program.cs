using Oracle.ManagedDataAccess.Client;
using MySql.Data.MySqlClient;
using System.Data;
using Microsoft.Extensions.Configuration;

namespace MPYakan
{
    internal class Program
    {
        // データベースコネクション
        private static OracleConnection? emCnn;
        private static MySqlConnection? mpCnn;
        // 各種データテーブル
        private static DataTable calendarDt = new();
        private static DataTable emDt = new();
        // 変数
        private static bool ret = false;

        // 夜間バッチ開始
        static void Main()
        {

            // 設定ファイルの存在確認
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, Common.APP_SETTING_FILE)))
            {
                Console.WriteLine($"設定ファイル[{Common.APP_SETTING_FILE}]が見つかりません．");
                Environment.Exit(9);
            }

            // 設定ファイルの読み込み
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(Common.APP_SETTING_FILE, optional: false, reloadOnChange: true)
                .Build();
            string? emSchema = config["EMSCHEMA"];
            string? mpSchema = config["MPSCHEMA"];
            Console.WriteLine($"EM[{emSchema}] -> MP[{mpSchema}]");

            // 設定ファイルの中身確認
            if ( string.IsNullOrEmpty( emSchema ) || string.IsNullOrEmpty( emSchema ))
            {
                Console.WriteLine($"設定ファイル[{Common.APP_SETTING_FILE}]の中身を確認してください．");
                Environment.Exit(9);
            }

            // EMデータベースコネクション開始
            if (!DBManager_Oracle.IsConnectOraSchema(ref emCnn, config))
            {
                Console.WriteLine("EM接続に失敗しました．");
                Environment.Exit(9);
            }

            // カレンダーマスタの読み込み
            if (!DBManager_Oracle.GetS0820YMD(emSchema, ref emCnn, ref calendarDt))
            {
                Console.WriteLine("カレンダー取得で異常が発生しました．");
                Environment.Exit(9);
            }

            // EMの対象手配データを読み込む
            if (!DBManager_Oracle.GetD0410ODRSTS(emSchema, ref emCnn, ref emDt))
            {
                Console.WriteLine("EMデータベース異常が発生しました．");
                Environment.Exit(9);
            }

            // EMデータベースコネクション終了
            DBManager_Oracle.CloseOraSchema(ref emCnn);



            // MPデータベースコネクション開始
            if (!DBManager_MySQL.IsConnectOraSchema(ref mpCnn, config))
            {
                Console.WriteLine("MP接続に失敗しました．");
                Environment.Exit(9);
            }

            // ①EMで実績計上された手配状態等を切削システムに取り込む
            // （MPPPSの処理と同じにする事！）
            Console.WriteLine(Common.MSG_SEPARATOR);
            Console.WriteLine("EM ステータス 取込処理開始");
            Console.WriteLine(Common.MSG_SEPARATOR);
            int updateCnt = DBManager_MySQL.UpdateODRSTS(mpSchema, ref mpCnn, ref emDt);
            if (updateCnt < 0)
            {
                "ステータス更新で異常が発生しました．".ConsoleWriteLinePadded();
                Environment.Exit(9);
            }
            if (updateCnt == 0)
                "EMステータスの更新はありませんでした．".ConsoleWriteLinePadded();
            if (updateCnt > 0)
                $"EM実績 {updateCnt:#,0}件を取り込み、同期をとりました．".ConsoleWriteLinePadded();


            // ②生産ダッシュボード処理
            // kd8490:切削手配遅れファイルの作成
            Console.WriteLine(Common.MSG_SEPARATOR);
            Console.WriteLine("生産ダッシュボード処理 [kd8490：切削手配遅れファイル]");
            Console.WriteLine(Common.MSG_SEPARATOR);
            int insertCnt = DBManager_MySQL.BackupBehindSchedule(mpSchema, ref mpCnn);
            if (insertCnt < 0)
            {
                "生産ダッシュボード処理で異常が発生しました．".ConsoleWriteLinePadded();
            }


            // ③注文ダッシュボード処理
            Console.WriteLine(Common.MSG_SEPARATOR);
            Console.WriteLine("注文ダッシュボード処理 [kd8510：切削オーダー集計ファイル]");
            Console.WriteLine(Common.MSG_SEPARATOR);

            // kd8510:切削オーダー集計ファイルの作成
            ret = DBManager_MySQL.HowManyOrders(mpSchema, ref mpCnn, ref calendarDt);
            if (!ret)
                "切削オーダー集計処理に失敗しましたが処理は続行します．".ConsoleWriteLinePadded();


            // 実績集計（月ごとの実績数などダッシュボードで表示すべきデータを集計）


            // コネクションの削除
            DBManager_MySQL.CloseMySqlSchema(ref mpCnn);

            // 終わり
            Console.WriteLine(Common.MSG_SEPARATOR);

        }


        // Oracle Sample
        private static void OracleSample()
        {
            Console.WriteLine("Oracle Sample");
            string sql = "SELECT DEPTCD,DEPTNM FROM KM0020";
            try
            {
                using OracleConnection conn = new();
                conn.ConnectionString =
                    "User ID=; Password=; Data Source=/";
                conn.Open();
                using OracleCommand cmd = new(sql);
                cmd.Connection = conn;
                cmd.CommandType = CommandType.Text;
                using OracleDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    Console.WriteLine(reader["DEPTCD"] + ":"
                                    + reader["DEPTNM"]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message.ToString());
            }
        }

        private static void MySQLSample()
        {
            Console.WriteLine("MySQL Sample");
            string sql = "SELECT DEPTCD,DEPTNM FROM KM0020";
            string ConnectionString =
                "Server=;"
                + "Port=;"
                + "Database=;"
                + "User ID=;"
                + "Password=;";
            try
            {
                using MySqlConnection conn = new(ConnectionString);
                conn.Open();
                using MySqlCommand cmd = new(sql, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    Console.WriteLine(reader["DEPTCD"] + ":"
                                    + reader["DEPTNM"]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message.ToString());
            }
        }
    }
}
