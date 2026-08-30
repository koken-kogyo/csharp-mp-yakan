using Oracle.ManagedDataAccess.Client;
using MySql.Data.MySqlClient;
using System.Data;
using System.Diagnostics;

namespace MPYakan
{
    internal class Program
    {
        // データベースコネクション
        private static OracleConnection? emCnn;
        private static MySqlConnection? mpCnn;
        private static string? emSchema;
        private static string? mpSchema;
        // 各種データテーブル
        private static DataTable calendarDt = new();
        private static DataTable controlDt = new();
        private static DataTable emDt = new();
        private static DataTable confirmedDt = new();
        // 変数
        private static bool ret = false;

        // 夜間バッチ開始
        static void Main()
        {

            // 初期化処理（事前準備）
            Common.AppConfig config = Common.LoadConfig();
            emSchema = config.EmConfig.SCHEMA;
            mpSchema = config.MpConfig.SCHEMA;
            Console.WriteLine($"EM[{emSchema}] -> MP[{mpSchema}]");

            // EMデータベースコネクション開始
            if (!DBManager_Oracle.IsConnectOraSchema(ref emCnn, config.EmConfig))
            {
                Console.WriteLine("EM接続に失敗しました．");
                Environment.Exit(9);
            }

            // カレンダーマスタ（前１か月、後２か月）読み込み
            // 手配先管理期間マスタ[KEY=60600]の読み込み
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

            // 当日の確定受注に特定の品番が登録されていないかチェック
            //  R1431-62111-70A(R1411-07534)(SW:R1411-07534-7)
            //  129H01-59560(SS:129H01-59560-2), 129H01-59570(SS:129H01-59570-2)
            //  129H01-59560(SS:129H01-59560-2), 129H01-59570(SS:129H01-59570-2)
            if (!DBManager_Oracle.GetD0010Confirmed(emSchema, ref emCnn, ref confirmedDt))
            {
                Console.WriteLine("EMデータベース異常が発生しました．");
                Environment.Exit(9);
            }



            // EM関連の処理終わり



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
            else if (updateCnt == 0)
            {
                "EMステータスの更新はありませんでした．".ConsoleWriteLinePadded();
            }
            else
            {
                $"EM実績 {updateCnt:#,0}件を取り込み、同期をとりました．".ConsoleWriteLinePadded();
            }


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
            {
                "切削オーダー集計処理で異常が発生しました．（処理は続行します）".ConsoleWriteLinePadded();
            }


            // ④内示生産処理
            /* 2026.07.26 一旦処理廃止
            var sw = Stopwatch.StartNew();
            Console.WriteLine(Common.MSG_SEPARATOR);
            Console.WriteLine("内示生産処理 [kd8500：内示生産管理ファイル]");
            Console.WriteLine(Common.MSG_SEPARATOR);
            int 内示更新件数 = 内示生産処理();
            if (内示更新件数 < 0)
            {
                "内示生産処理で異常が発生しました．".ConsoleWriteLinePadded();
            }
            else if (内示更新件数 == 0)
            {
                "内示生産処理の更新はありませんでした．".ConsoleWriteLinePadded();
            }
            else
            {
                $"内示生産管理ファイルを {内示更新件数:#,0} 件更新しました．".ConsoleWriteLinePadded();
            }
            sw.Stop();
            $"処理時間: {sw.ElapsedMilliseconds} ms".ConsoleWriteLinePadded();
            */

            // ⑤通知データ登録処理
            if (confirmedDt.Rows.Count > 0)
            {
                Console.WriteLine(Common.MSG_SEPARATOR);
                Console.WriteLine("通知データ登録 [kd8520：切削通知ファイル]");
                Console.WriteLine(Common.MSG_SEPARATOR);
                int insertKD8520Cnt = DBManager_MySQL.InsertKD8520(mpSchema, ref mpCnn, confirmedDt);
                if (insertKD8520Cnt > 0)
                {
                    $"通知データを {insertKD8520Cnt:#,0}件登録しました．".ConsoleWriteLinePadded();
                }
                else
                {
                    "通知データ登録処理で異常が発生しました．（処理は続行します）".ConsoleWriteLinePadded();
                }
            }

            // データベースコネクションの削除
            DBManager_Oracle.CloseOraSchema(ref emCnn);
            DBManager_MySQL.CloseMySqlSchema(ref mpCnn);

            // 終わり
            Console.WriteLine(Common.MSG_SEPARATOR);

        }



        // ④内示生産処理
        private static int 内示生産処理()
        {
            DataTable KM8435 = new();
            DataTable KD8500 = new();
            int insertCnt = 0;
            int updateCnt = 0;

            // ①共通部品マスタを取得
            if (!DBManager_MySQL.ReadKM8435(emSchema, ref mpCnn, ref KM8435))
                return -1;

            // ②内示生産管理ファイルを取得
            if (!DBManager_MySQL.ReadKD8500(mpSchema, ref mpCnn, ref KD8500))
                return -1;

            // ③対象の内示受注ファイル検索と集計した結果を取得
            DataTable targetDt = new();
            bool flg = DBManager_Oracle.GetD0030Target(emSchema, ref emCnn, ref KD8500, ref KM8435, ref targetDt);
            if (targetDt.Rows.Count == 0) return 0;

            // ④採番
            string odrno = DBManager_MySQL.GetLastNaijiNo(mpSchema, ref mpCnn); // YYMM900000
            if (odrno == "") return -1;
            string yymm = odrno[..4];
            int seq = int.Parse(odrno.Substring(4, 6));

            // トランザクション開始
            if (mpCnn == null)
            {
                Console.WriteLine("MPコネクションが確立されていません．");
                return -1;
            }
            MySqlTransaction transaction = mpCnn.BeginTransaction()!;
            try
            {
                foreach (DataRow row in targetDt.Rows)
                {
                    string hmcd = (string)row["HMCD"];
                    string targetyymm = (string)row["TARGETYYMM"];
                    int yyyy = int.Parse(targetyymm[..4]);
                    int mm = int.Parse(targetyymm.Substring(5, 2));
                    DateTime nextmonth = new DateTime(yyyy, mm, 1).AddMonths(1); // 差分を翌月に集約
                    decimal qty = (decimal)row["JUQTY"];
                    var r = KD8500.AsEnumerable()
                        .FirstOrDefault(r =>
                            r.Field<string>("MCGCD") == "XT" &&
                            r.Field<string>("HMCD") == hmcd &&
                            r.Field<int>("YYYY") == yyyy
                            );
                    string lastyymm = "1900/01";
                    decimal lastqty = 0;
                    string productkbn = "";
                    if (r != null)
                    {
                        lastqty = Convert.ToDecimal(r["LASTQTY"]);
                        lastyymm = r.Field<string>("LASTYYMM") ?? "1900/01";
                        productkbn = r.Field<string>("PDTKBN") ?? "1";
                        if (lastyymm == targetyymm && lastqty == qty) continue; // 前回内示から変化なしの場合次の品番へ
                        r["LASTYYMM"] = targetyymm;                 // 今回対象月
                        r["LASTDT"] = row["LASTDT"];                // 今回最新登録日時
                        if (lastyymm != targetyymm)
                        {
                            // 内示生産管理ファイルの値変更
                            r["UPDCNT"] = 0;                        // 更新回数
                            r["LASTQTY"] = qty;                     // 前回内示
                            r[$"M{mm:00}"] = qty;                   // 内示数

                            // ⑤内示受注を切削手配ファイルに登録
                            seq++;
                            string newOdrno = $"{yymm}{seq:000000}";
                            if (DBManager_MySQL.InsertKD8430KD8450(mpSchema, ref mpCnn, row, ref seq, newOdrno, targetyymm, lastyymm, lastqty, productkbn))
                            {
                                insertCnt++;
                            }
                            else
                            {
                                throw new Exception();
                            }
                        }
                        else
                        {
                            int nextmm = nextmonth.Month;
                            int nextqty = (int)r[$"M{nextmm:00}"];
                            r["UPDCNT"] = (int)r["UPDCNT"] + 1;
                            r["LASTQTY"] = qty;                     // 前回内示
                            r[$"M{nextmm:00}"] = nextqty + qty - lastqty;     // 差分は翌月の内示数
                        }
                        r["UPDTDT"] = DateTime.Now;                 // 更新日時
                        updateCnt++;
                    }
                    else
                    {
                        continue;
                    }

                }
                // ⑥内示生産管理ファイルを更新
                if (updateCnt > 0)
                {
                    int cnt = DBManager_MySQL.UpdateKD8500(mpSchema, ref mpCnn, ref KD8500);
                    if (cnt != updateCnt) throw new Exception($"データベースの更新件数に差異:{cnt}:{updateCnt}");
                }

                // トランザクション終了
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine("内示生産処理で例外が発生しました．" + ex.Message);
                try
                {
                    transaction.Rollback();
                    Console.WriteLine("トランザクションをロールバックしました．");
                }
                catch (Exception rollBackEx)
                {
                    Console.WriteLine("トランザクションのロールバックに失敗しました．" + rollBackEx.Message);
                }
                return -1;
            }

            return updateCnt;
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
                using MySqlDataReader reader = cmd.ExecuteReader();
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
