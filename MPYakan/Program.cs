using Oracle.ManagedDataAccess.Client;
using MySql.Data.MySqlClient;
using System.Data;

namespace MPYakan
{
    internal class Program
    {
        // データベースコネクション
        private static OracleConnection? emCnn;
        private static MySqlConnection? mpCnn;
        // 各種データテーブル
        private static DataTable calendarDt = new();
        private static DataTable controlDt = new();
        private static DataTable emDt = new();
        // 変数
        private static bool ret = false;

        // 夜間バッチ開始
        static void Main()
        {

            // 初期化処理（事前準備）
            Common.AppConfig config = Common.LoadConfig();
            string emSchema = config.EmConfig.SCHEMA;
            string mpSchema = config.MpConfig.SCHEMA;
            Console.WriteLine($"EM[{emSchema}] -> MP[{mpSchema}]");

            // EMデータベースコネクション開始
            if (!DBManager_Oracle.IsConnectOraSchema(ref emCnn, config.EmConfig))
            {
                Console.WriteLine("EM接続に失敗しました．");
                Environment.Exit(9);
            }

            // カレンダーマスタ（前１か月、後２か月）と手配先管理期間マスタ[60600]の読み込み
            if (!DBManager_Oracle.GetYMD(emSchema, ref emCnn, ref calendarDt, ref controlDt))
            {
                Console.WriteLine("マスタ取得で異常が発生しました．");
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
            if (!DBManager_MySQL.IsConnectOraSchema(ref mpCnn, config.MpConfig))
            {
                Console.WriteLine("MP接続に失敗しました．");
                Environment.Exit(9);
            }

            // ①EMで実績計上された手配状態等を切削システムに取り込む
            // （MPPPSソースとロジックが2か所となるので編集する際は、同じにする事！）
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
            Console.WriteLine(Common.MSG_SEPARATOR);
            Console.WriteLine("生産ダッシュボード処理 [kd8490：切削手配遅れファイル]");
            Console.WriteLine(Common.MSG_SEPARATOR);
            int insertCnt = DBManager_MySQL.BackupBehindSchedule(mpSchema, ref mpCnn);
            if (insertCnt < 0)
            {
                "切削手配遅れファイル作成で異常が発生しました．（処理は続行します）".ConsoleWriteLinePadded();
            }


            // ③注文ダッシュボード処理
            Console.WriteLine(Common.MSG_SEPARATOR);
            Console.WriteLine("注文ダッシュボード処理 [kd8510：切削オーダー集計ファイル]");
            Console.WriteLine(Common.MSG_SEPARATOR);
            ret = DBManager_MySQL.HowManyOrders(mpSchema, ref mpCnn, ref calendarDt, ref controlDt);
            if (!ret)
                "切削オーダー集計処理で異常が発生しました．（処理は続行します）".ConsoleWriteLinePadded();


            // ④見込み生産処理
            Console.WriteLine(Common.MSG_SEPARATOR);
            Console.WriteLine("見込み生産処理 [kd8500：見込生産管理ファイル]");
            Console.WriteLine(Common.MSG_SEPARATOR);
            ret = DBManager_MySQL.Plan2Order(mpSchema, ref mpCnn, ref calendarDt);
            if (!ret)
            {
                "見込生産処理で異常が発生しました．".ConsoleWriteLinePadded();
            }



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
