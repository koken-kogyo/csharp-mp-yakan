using DecryptPassword;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text;

namespace MPYakan
{
    public static class DBManager_MySQL
    {
        /// <summary>
        /// Oracle データベース スキーマ接続成否
        /// </summary>
        /// <param name="mpCnn">MySQL データベースへの接続クラス</param>
        /// <returns>結果 (false: 失敗, true: 成功)</returns>
        public static bool IsConnectOraSchema(ref MySqlConnection? mpCnn, Common.MpConfig mpConfig)
        {
            bool ret;

            // appsettings.json からデータベース情報を読み込む
            string server = mpConfig.SERVER;
            string database = mpConfig.SCHEMA;
            int port = Convert.ToInt32(mpConfig.PORT);
            string uid = mpConfig.USER;
            string charset = "utf8mb4";

            // パスワード復号化
            var dpc = new DecryptPasswordClass();
            string encpassed = mpConfig.PASS;
            dpc.DecryptPassword(encpassed, out string decPasswd);
            string pwd = decPasswd;

            // MySQL 接続文字列
            string connectionString = string.Format(
                "Server={0};Database={1};Port={2};Uid={3};Pwd={4};Charset={5}"
                , server, database, port, uid, pwd, charset);

            try
            {
                // MySQL へのコネクションの確立
                mpCnn = new MySqlConnection(connectionString);
                mpCnn.Open();
                ret = true;
            }
            catch
            {
                // 接続を閉じる
                CloseMySqlSchema(ref mpCnn);
                ret = false;
            }
            return ret;
        }

        /// <summary>
        /// MySQL データベース スキーマからの切断
        /// </summary>
        /// <param name="mpCnn">MySQL データベースへの接続クラス</param>
        public static void CloseMySqlSchema(ref MySqlConnection? mpCnn)
        {
            mpCnn?.Close();
        }

        /// <summary>
        /// 切削手配ファイル受注状態ミラーリング
        /// EMの手配状態をMPシステムにミラーリング（3,4,9になった状態をMPに反映)
        /// （9:取消は手動でやってもらうためにミラーリング対象外）＝＞（やっぱり自動で更新してしまう）
        /// </summary>
        /// <param name="emDt">EMの手配ファイル</param>
        /// <returns>更新件数</returns>
        public static int UpdateODRSTS(string? mpSchema, ref MySqlConnection? mpCnn, ref DataTable emDt)
        {
            int ret = -1;
            string fromDt = DateTime.Now.AddDays(-31).ToString("yyyy/MM/dd");
            string toDt = DateTime.Now.AddDays(14).ToString("yyyy/MM/dd");

            try
            {
                // 切削手配ファイルミラーリング:KD8430
                using (var adapter = new MySqlDataAdapter())
                {
                    var dtUpdate = new DataTable();
                    var countUpdate = 0;
                    var countDelete = 0;
                    string sql = "SELECT ODRNO, ODRSTS, JIQTY, DENPYODT, UPDTID, UPDTDT, MPUPDTID "
                        + "FROM "
                        + mpSchema + ".KD8430 "
                        + "WHERE "
                        + "ODCD like '6060%' "
                        + "and ODRSTS in ('1','2','3','9') "
                        + $"and EDDT between '{fromDt}' and '{toDt}' "
                    ;
                    adapter.SelectCommand = new MySqlCommand(sql, mpCnn);
                    using var buider = new MySqlCommandBuilder(adapter);
                    adapter.Fill(dtUpdate);

                    foreach (DataRow r in dtUpdate.Rows)
                    {
                        string? MpSTS = r["ODRSTS"].ToString();
                        DataRow[] drEM = emDt.Select($"ODRNO='{r["ODRNO"]}'");
                        if (drEM.Length == 1)
                        {
                            // EMのステータスと相違がないかチェック 2:確定、3:着手、4:完了、9:取消
                            string? EmSTS = drEM[0]["ODRSTS"].ToString();
                            if (MpSTS == "1" && EmSTS == "2") continue;
                            if (MpSTS != EmSTS)
                            {
                                r["ODRSTS"] = drEM[0]["ODRSTS"];
                                r["JIQTY"] = drEM[0]["JIQTY"];
                                r["DENPYODT"] = drEM[0]["DENPYODT"];
                                r["UPDTID"] = drEM[0]["UPDTID"];
                                r["UPDTDT"] = drEM[0]["UPDTDT"];
                                r["MPUPDTID"] = "YAKAN";
                                countUpdate++;
                            }
                        }
                        else if (drEM.Length == 0 && MpSTS == "9")
                        {
                            r.Delete();
                            countDelete++;
                        }
                    }
                    // 更新があればデータベースへの一括更新
                    if (countUpdate + countDelete > 0)
                    {
                        adapter.Update(dtUpdate);
                    }
                    // 更新件数の返却
                    ret = countUpdate + countDelete;
                }

                // 切削オーダーファイルミラーリング:KD8450
                using (var adapter = new MySqlDataAdapter())
                {
                    var dtUpdate = new DataTable();
                    var countUpdate = 0;
                    var countDelete = 0;
                    string sql = "SELECT ODRNO, MPSEQ, LOTSEQ, ODRSTS, JIQTY, MPUPDTID "
                        + "FROM "
                        + mpSchema + ".KD8450 "
                        + "WHERE "
                        + "ODRSTS in ('1','2','3','9') "
                        + $"and EDDT between '{fromDt}' and '{toDt}' "
                    ;
                    adapter.SelectCommand = new MySqlCommand(sql, mpCnn);
                    using var buider = new MySqlCommandBuilder(adapter);
                    adapter.Fill(dtUpdate);

                    foreach (DataRow r in dtUpdate.Rows)
                    {
                        string? MpSTS = r["ODRSTS"].ToString();
                        DataRow[] drEM = emDt.Select($"ODRNO='{r["ODRNO"]}'");
                        if (drEM.Length == 1)
                        {
                            // EMのステータスと相違がないかチェック 2:確定、3:着手、4:完了、9:取消
                            string? EmSTS = drEM[0]["ODRSTS"].ToString();
                            if (MpSTS == "1" && EmSTS == "2") continue;
                            if (MpSTS != EmSTS)
                            {
                                r["ODRSTS"] = drEM[0]["ODRSTS"];
                                r["JIQTY"] = drEM[0]["JIQTY"];
                                r["MPUPDTID"] = "YAKAN";
                                countUpdate++;
                            }
                        }
                        else if (drEM.Length == 0 && MpSTS == "9")
                        {
                            r.Delete();
                            countDelete++;
                        }
                    }
                    // 更新があればデータベースへの一括更新
                    if (countUpdate + countDelete > 0)
                    {
                        adapter.Update(dtUpdate);
                    }
                }


            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return ret;
        }

        /// <summary>
        /// 当日切削手配遅れファイルの作成
        /// </summary>
        /// <returns>挿入件数</returns>
        public static int BackupBehindSchedule(string? mpSchema, ref MySqlConnection? mpCnn)
        {
            int ret = -1;
            string sql;
            if (mpCnn is null || mpSchema is null)
            {
                "MySQL 接続が確立されていません．".ConsoleWriteLinePadded();
                return ret;
            }
            using (MySqlTransaction txn = mpCnn.BeginTransaction())
            {
                try
                {
                    var countDelete = 0;
                    var countInsert = 0;

                    // ①翌営業日の算出
                    var calendarDt = new DataTable();
                    sql = "select min(YMD) as YMD from s0820 where caltyp='00001' and wkkbn='1' " +
                        "and YMD>curdate()";
                    using (MySqlCommand myCmd = new(sql, mpCnn))
                    {
                        using MySqlDataAdapter myDa = new(myCmd);
                        myDa.Fill(calendarDt);
                    }
                    if (calendarDt.Rows.Count == 0)
                    {
                        // ⑥異常の場合はロールバック
                        "翌営業日の取得に失敗しました．".ConsoleWriteLinePadded();
                        return ret;
                    }
                    var targetDate = calendarDt.Rows[0]["YMD"];


                    // ②削除（1日2回の実行に備える）
                    sql = "delete from " + mpSchema + ".kd8490 " +
                        $"where TARGETDT = '{targetDate}'";
                    ;
                    using (MySqlCommand myCmd = new(sql, mpCnn))
                    {
                        countDelete = myCmd.ExecuteNonQuery();
                    }

                    // ③明営業日の未完了をテーブルそのまま挿入
                    sql = "insert into " + mpSchema + ".kd8490 " +
                        $"select '{targetDate}' " +
                        ",ODRNO,HMCD,ODRQTY,JIQTY,ODRSTS,EDDT,EDTIM,'YAKAN',now() " +
                        "from " + mpSchema + ".kd8430 " +
                        "where ODRSTS in ('1','2','3') and ODCD like '6060%' and " +
                        $"eddt between date_add(now(), interval -1 month) and '{targetDate}'"
                    ;
                    using (MySqlCommand myCmd = new(sql, mpCnn))
                    {
                        countInsert = myCmd.ExecuteNonQuery();
                    }

                    // ④トランザクションのコミット
                    txn.Commit();

                    // ⑤戻り値は挿入した件数
                    if (countInsert > 0)
                    {
                        $"翌日の遅れ分 {countInsert:#,0}件 を登録しました．".ConsoleWriteLinePadded();
                    }
                    ret = countInsert;

                }
                catch (Exception ex)
                {
                    // ⑥異常の場合はロールバック
                    txn.Rollback();
                    "遅れ処理はロールバックしました．".ConsoleWriteLinePadded();
                    Console.WriteLine(ex.Message);
                }
            }
            return ret;
        }

        /// <summary>
        /// 切削オーダーファイル集計（注文ダッシュボード用）
        /// リアルタイムで集計するのはサーバーの負荷が大きすぎるので
        /// 夜間バッチor手配取込時に計算してKD8510:集計テーブルを作成
        /// 先週・今週・来週の3週間分をリフレッシュ
        /// </summary>
        /// <param name="emDt">EMの手配ファイル</param>
        /// <returns>終了状態</returns>
        public static bool HowManyOrders(string? mpSchema, ref MySqlConnection? mpCnn, ref DataTable S0820, ref DataTable M0340)
        {
            bool ret = false;
            if (mpCnn is null || mpSchema is null)
            {
                "MySQL 接続が確立されていません．".ConsoleWriteLinePadded();
                return ret;
            }
            using (MySqlTransaction txn = mpCnn.BeginTransaction())
            {
                try
                {
                    DateTime[] fromDt = new DateTime[3];      // 0：先週、1：今週、2：来週
                    DateTime[] toDt = new DateTime[3];        // 0：先週、1：今週、2：来週

                    fromDt[1] = (DateTime)M0340.Rows[0]["前回確定開始日"];
                    toDt[1] = (DateTime)M0340.Rows[0]["前回確定終了日"];
                    fromDt[2] = (DateTime)M0340.Rows[0]["今回確定開始日"];
                    toDt[2] = (DateTime)M0340.Rows[0]["今回確定終了日"];

                    // 稼働日ベースでの先週の日付をカレンダーテーブルから取得
                    DateTime prevStartDay = fromDt[1];
                    int workingDaysCount = 0;
                    while (workingDaysCount == 0)
                    {
                        prevStartDay = prevStartDay.AddDays(-7);
                        workingDaysCount = S0820.AsEnumerable()
                        .Where(row =>
                            row.Field<DateTime>("YMD") >= prevStartDay &&
                            row.Field<DateTime>("YMD") <= prevStartDay.AddDays(5))
                        .Count();
                    }
                    fromDt[0] = prevStartDay;
                    toDt[0] = prevStartDay.AddDays(5);

                    int countInsert = 0;
                    int countDelete = 0;

                    // ①再計算用するため対象を全削除
                    string sql = "delete from " + mpSchema + ".kd8510 where EDDT between " +
                        $"'{fromDt[0]}' and '{toDt[2]}'";
                    using (MySqlCommand myCmd = new(sql, mpCnn))
                    {
                        countDelete = myCmd.ExecuteNonQuery();
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        // ②SW工程（週の段取り回数合計を手配日で割って取得するパターン）
                        sql = "insert into " + mpSchema + ".kd8510 " +
                            // サブクエリで週合計段取り回数を日当たりで割った回数を取得
                            "with w as " +
                            "(" +
                                "select MCGCD, MCCD, truncate(count(distinct MATESIZE) / count(distinct EDDT), 2) as SETUPNUM " +
                                "from " + mpSchema + ".kd8450 a " +
                                "inner join " + mpSchema + ".km8430 m30 on m30.HMCD=a.HMCD " +
                                "where a.ODRSTS <> '9' " +
                                    $"and a.EDDT between '{fromDt[i]}' and '{toDt[i]}' " +
                                    "and concat(a.MCGCD,'-',a.MCCD) in ('SW-SW') " +
                                "group by a.MCGCD, a.MCCD" +
                            ")" +
                            // 本体クエリで日ごとの稼働明細を集計したレコード
                            "select z.EDDT, z.MCGCD, z.MCCD, m20.KTNKBN" +
                                ", count(z.HMCD) as アイテム数" +
                                ", sum(z.ODRQTY) as 注文本数" +
                                ", sum(z.OT) as 稼働時間" +
                                ", round(sum(z.OT) / 3600, 2) as 稼働時間h" +
                                ", w.SETUPNUM as 段取り回数" +
                                ", w.SETUPNUM * m20.SETUPTM2 as 段取り時間" +
                                ", round(w.SETUPNUM * m20.SETUPTM2 / 3600, 2) as 段取り時間h" +
                                ",'YAKAN' as 登録者" +
                                ", now() as 登録日時 " +
                            "from " +
                            "(" +
                                // サブクエリで稼働明細を取得
                                "select a.EDDT, a.MCGCD, a.MCCD, a.HMCD, m30.MATESIZE, sum(a.ODRQTY) as ODRQTY" +
                                    ",case " +
                                        "when a.MCGCD=m30.KT1MCGCD and a.MCCD=m30.KT1MCCD then sum(a.ODRQTY) * ifnull(m30.KT1CT,0)" +
                                        "when a.MCGCD=m30.KT2MCGCD and a.MCCD=m30.KT2MCCD then sum(a.ODRQTY) * ifnull(m30.KT2CT,0)" +
                                        "when a.MCGCD=m30.KT3MCGCD and a.MCCD=m30.KT3MCCD then sum(a.ODRQTY) * ifnull(m30.KT3CT,0)" +
                                        "when a.MCGCD=m30.KT4MCGCD and a.MCCD=m30.KT4MCCD then sum(a.ODRQTY) * ifnull(m30.KT4CT,0)" +
                                        "when a.MCGCD=m30.KT5MCGCD and a.MCCD=m30.KT5MCCD then sum(a.ODRQTY) * ifnull(m30.KT5CT,0)" +
                                        "when a.MCGCD=m30.KT6MCGCD and a.MCCD=m30.KT6MCCD then sum(a.ODRQTY) * ifnull(m30.KT6CT,0)" +
                                        "else 0 " +
                                    "end as OT " +
                                "from " + mpSchema + ".kd8450 a " +
                                "inner join " + mpSchema + ".km8430 m30 on m30.HMCD=a.HMCD " +
                                "where a.ODRSTS <> '9' " +
                                    $"and a.EDDT between '{fromDt[i]}' and '{toDt[i]}' " +
                                    "and concat(a.MCGCD,'-',a.MCCD) in ('SW-SW') " +
                                "group by a.EDDT,a.MCGCD,a.MCCD,a.HMCD" +
                            ") z, " + mpSchema + ".km8420 m20, w " +
                            "where z.MCGCD=m20.MCGCD and z.MCCD=m20.MCCD and w.MCGCD=z.MCGCD and w.MCCD=z.MCCD " +
                            "group by z.EDDT, z.MCGCD, z.MCCD, w.SETUPNUM, m20.KTNKBN, m20.SETUPTM2 " +
                            "order by z.EDDT, z.MCGCD, z.MCCD"
                        ;
                        using (MySqlCommand myCmd = new(sql, mpCnn))
                        {
                            countInsert = myCmd.ExecuteNonQuery();
                        }
                        // ③SW工程以外
                        sql = "insert into " + mpSchema + ".kd8510 " +
                            // 本体クエリで日ごとの稼働明細を集計したレコード
                            "select z.EDDT, z.MCGCD, z.MCCD, m20.KTNKBN" +
                                ", count(z.HMCD) as アイテム数" +
                                ", sum(z.ODRQTY) as 注文本数" +
                                ", case when z.MCCD='S500' then sum(z.OT) / 2 else sum(z.OT) end as 稼働時間" +
                                ", case when z.MCCD='S500' then round(sum(z.OT) / 2 / 3600, 2) else round(sum(z.OT) / 3600, 2) end as 稼働時間h" +
                                ", case when z.MCCD='S500' then count(distinct z.MATESIZE) / 2 else count(distinct z.MATESIZE) end as 段取り回数" +
                                ", case when z.MCCD='S500' then count(distinct z.MATESIZE) / 2 * m20.SETUPTM1 else count(distinct z.MATESIZE) * m20.SETUPTM1 end as 段取り時間" +
                                ", case when z.MCCD='S500' then round(count(distinct z.MATESIZE) / 2 * m20.SETUPTM1 / 3600, 2) else round(count(distinct z.MATESIZE) * m20.SETUPTM1 / 3600, 2) end as 段取り時間h" +
                                ",'YAKAN' as 登録者" +
                                ", now() as 登録日時 " +
                            "from " +
                            "(" +
                                // サブクエリで稼働明細を取得
                                "select a.EDDT, a.MCGCD, a.MCCD, a.HMCD, m30.MATESIZE, sum(a.ODRQTY) as ODRQTY" +
                                    ",case " +
                                        "when a.MCGCD=m30.KT1MCGCD and a.MCCD=m30.KT1MCCD then sum(a.ODRQTY) * ifnull(m30.KT1CT,0)" +
                                        "when a.MCGCD=m30.KT2MCGCD and a.MCCD=m30.KT2MCCD then sum(a.ODRQTY) * ifnull(m30.KT2CT,0)" +
                                        "when a.MCGCD=m30.KT3MCGCD and a.MCCD=m30.KT3MCCD then sum(a.ODRQTY) * ifnull(m30.KT3CT,0)" +
                                        "when a.MCGCD=m30.KT4MCGCD and a.MCCD=m30.KT4MCCD then sum(a.ODRQTY) * ifnull(m30.KT4CT,0)" +
                                        "when a.MCGCD=m30.KT5MCGCD and a.MCCD=m30.KT5MCCD then sum(a.ODRQTY) * ifnull(m30.KT5CT,0)" +
                                        "when a.MCGCD=m30.KT6MCGCD and a.MCCD=m30.KT6MCCD then sum(a.ODRQTY) * ifnull(m30.KT6CT,0)" +
                                        "else 0 " +
                                    "end as OT " +
                                "from " + mpSchema + ".kd8450 a " +
                                "inner join " + mpSchema + ".km8430 m30 on m30.HMCD=a.HMCD " +
                                "where a.ODRSTS <> '9' " +
                                    $"and a.EDDT between '{fromDt[i]} ' and ' {toDt[i]}' " +
                                    "and concat(a.MCGCD,'-',a.MCCD) in " +
                                    "(" +
                                        "'NC-4','NC-5','NC-6','NC-7','NC-8'," +
                                        "'MC-3B','MC-3F','MC-CL','3BP-3BI','3BP-3BP','ON-S500'," +
                                        "'LF-LF'," +
                                        "'SS-SS','XT-XT','CN-CN1','CN-CN2','CN-CN3','CN-CN4'," +
                                        "'MS-1','MS-2','MS-3','MS-4','MS-5','MS-6','SK-SK2'," +
                                        "'TN-2','TN-3','TN-4','TN-5','TN-6'" +
                                    ") " +
                                "group by a.EDDT,a.MCGCD,a.MCCD,a.HMCD" +
                            ") z, " + mpSchema + ".km8420 m20 " +
                            "where z.MCGCD=m20.MCGCD and z.MCCD=m20.MCCD " +
                            "group by z.EDDT, z.MCGCD, z.MCCD, m20.KTNKBN, m20.SETUPTM1, m20.SETUPTM2 " +
                            "order by z.EDDT, z.MCGCD, z.MCCD"
                        ;
                        using (MySqlCommand myCmd = new(sql, mpCnn))
                        {
                            countInsert += myCmd.ExecuteNonQuery();
                        }
                        if (countInsert > 0)
                        {
                            $"{fromDt[i]:M}～{toDt[i]:M} 再計算をして {countInsert:##,0}件 を更新しました．".ConsoleWriteLinePadded();
                        }
                    }
                    txn.Commit();
                    ret = true;
                }
                catch (Exception ex)
                {
                    txn.Rollback();
                    "集計処理はロールバックしました．".ConsoleWriteLinePadded();
                    Console.WriteLine(ex.Message);
                }
            }
            return ret;
        }

        /// <summary>
        /// 共通部品マスタの読み込み
        /// </summary>
        /// <returns></returns>
        public static bool ReadKM8435(string? mpSchema, ref MySqlConnection? mpCnn, ref DataTable KM8435)
        {
            if (mpCnn is null || mpSchema is null)
            {
                "MySQL 接続が確立されていません．".ConsoleWriteLinePadded();
                return false;
            }
            using MySqlDataAdapter adapter = new (new MySqlCommand("select * from km8435", mpCnn));
            adapter.Fill(KM8435);
            return (KM8435.Rows.Count > 0);
        }

        /// <summary>
        /// 内示生産管理ファイルの読み込み
        /// </summary>
        /// <returns></returns>
        public static bool ReadKD8500(string? mpSchema, ref MySqlConnection? mpCnn, ref DataTable KD8500)
        {
            if (mpCnn is null || mpSchema is null)
            {
                "MySQL 接続が確立されていません．".ConsoleWriteLinePadded();
                return false;
            }
            DateTime d = DateTime.Now;
            var sql = "select * from kd8500 where concat(MCGCD,HMCD,YYYY) in " +
                $"(select concat(MCGCD,HMCD,max(YYYY)) from kd8500 where yyyy<={d:yyyy} group by MCGCD,HMCD)";
            using MySqlDataAdapter adapter = new(new MySqlCommand(sql, mpCnn));
            adapter.Fill(KD8500);
            return (KD8500.Rows.Count > 0);
        }

        /// <summary>
        /// 内示生産管理ファイルの更新
        /// </summary>
        /// <returns></returns>
        public static int UpdateKD8500(string? mpSchema, ref MySqlConnection? mpCnn, ref DataTable KD8500)
        {
            if (mpCnn is null || mpSchema is null)
            {
                "MySQL 接続が確立されていません．".ConsoleWriteLinePadded();
                return -1;
            }
            using MySqlDataAdapter adapter = new(new MySqlCommand("select * from kd8500 limit 0", mpCnn));
            using MySqlCommandBuilder buider = new(adapter);
            DataTable dt = new();
            adapter.Fill(dt);
            return adapter.Update(KD8500);
        }

        // 最終内示手配Noを取得
        public static string GetLastNaijiNo(string? mpSchema, ref MySqlConnection? mpCnn)
        {
            var sql = @$"
                select ifnull(max(ODRNO),
                    concat(date_format(now() - interval 0 month, '%y'), date_format(now() - interval 0 month, '%m'), '900000')) as ODRNO
                from {mpSchema}.kd8430 where ODCD = '60699' and ODRNO between
                    concat(date_format(now() - interval 0 month, '%y'), date_format(now() - interval 0 month, '%m'), '900000') and
                    concat(date_format(now() - interval 0 month, '%y'), date_format(now() - interval 0 month, '%m'), '999999')";
            // Debug用に interval 0 は残しておく（-1でテストは行う）
            string odrno;
            using (MySqlCommand cmd = new(sql, mpCnn))
            {
                // ExecuteScalar():１件１項目の場合に使用できるメソッド
                // odrno = cmd.ExecuteScalar().ToString();          // SQLでNULLを潰していて実害ゼロなのに VS がうるさい
                // odrno = Convert.ToString(cmd.ExecuteScalar());   // (C# 1.0) Convert.ToString は null を空文字に変換してくれる
                // odrno = cmd.ExecuteScalar()?.ToString() ?? "";   // (C# 6.0) [?.]（null 条件演算子）Visual Studio の波線、実害はなくても精神衛生に悪いんですよね。
                odrno = cmd.ExecuteScalar().ToString()!;            // (C# 8.0) [! ]（null 許容抑制）null ではないことを明示する演算子
            }
            return odrno;
        }

        /// <summary>
        /// 見込手配登録処理
        /// </summary>
        /// <param name="row">登録データ</param>
        /// <returns>登録件数</returns>
        public static bool InsertKD8430KD8450(string? mpSchema, ref MySqlConnection? mpCnn, DataRow row
            , ref int seq, string newOdrno
            , string targetyymm, string lastyymm, decimal lastqty, string productkbn)
        {
            bool ret = false;
            string insertSQL = "";
            try
            {
                string hmcd = row["HMCD"].ToString()!;
                DateTime eddt = (DateTime)row["JUDT"];
                decimal odrqty = Convert.ToDecimal(row["JUQTY"]);

                // ①品目手順マスタから登録に必要な情報を取得
                var sql = $@"
                    select m.KTSEQ, m.KTCD, m.ODCD, m.ODRLT, m.WKNOTE, m.WKCOMMENT
                        ,KTSU, KT1MCGCD, KT1MCCD, KT2MCGCD, KT2MCCD,KT3MCGCD, KT3MCCD
                        ,KT4MCGCD, KT4MCCD, KT5MCGCD, KT5MCCD, KT6MCGCD, KT6MCCD
                    from {mpSchema}.m0510 m
                    join (
                        select HMCD, KTSEQ, max(VALDTF) AS VALDTF
                        from {mpSchema}.m0510
                        where HMCD='{hmcd}' and KTCD like 'MP%' and ODCD like '6060%' and JIKBN = '1'
                        group by HMCD, KTSEQ
                    ) x on m.HMCD = x.HMCD AND m.VALDTF = x.VALDTF and m.KTSEQ = x.KTSEQ
                    inner join KM8430 km on km.HMCD = m.HMCD
                    order by m.KTSEQ limit 1
                ";
                using MySqlCommand cmd = new(sql, mpCnn);
                using MySqlDataReader reader = cmd.ExecuteReader();
                reader.Read();  // 1行だけあればいいかな
                int ktseq = reader.GetInt32("KTSEQ");
                string ktcd = reader.GetString("KTCD");
                
                
                // 内示生産専用のコード"60699"に変換
                string odcd = "60699"; // reader.GetString("ODCD");
                

                int lttime = reader.GetInt32("ODRLT");                  // M0520.ODRLT：製造購買LT
                string wknote = reader.IsDBNull(reader.GetOrdinal("WKNOTE")) ? ""
                    : reader.GetString(reader.GetOrdinal("WKNOTE"));
                string wkcomment = reader.IsDBNull(reader.GetOrdinal("WKCOMMENT")) ? ""
                    : reader.GetString("WKCOMMENT");
                // 配列に格納
                int ktsu = reader.GetInt32("KTSU");
                string[] mcgcd = new string[ktsu];
                string[] mccd = new string[ktsu];
                for (int i = 0; i < ktsu; i++)
                {
                    mcgcd[i] = reader.GetString($"KT{i + 1}MCGCD");
                    mccd[i] = reader.GetString($"KT{i + 1}MCCD");
                }
                reader.Close();

                //// ②同月処理（完了予定日を翌営業日にスライドして差分を登録）
                //if (targetyymm == lastyymm)
                //{
                //    eddt = GetNextWorkDay(mpSchema, ref mpCnn, hmcd, eddt, productkbn);
                //    odrqty -= lastqty;
                //}

                // 今作ではXT工程のみでトライ
                if (mcgcd.Contains("XT") && mccd.Contains("XT"))
                {
                    // ③KD8430:切削手配ファイルの登録
                    if (productkbn == "3")
                    {
                        insertSQL = InsertMpOrderSQL(newOdrno, ktseq, hmcd, ktcd, odrqty, odcd, lttime, eddt, wknote, wkcomment);
                    }
                    // 生産区分が「２：内示平準」の場合、内示数を４週に分割して週の初めに登録
                    else if (productkbn == "2")
                    {
                        insertSQL = InsertMpDivideOrderSQL(newOdrno, ktseq, hmcd, ktcd, odrqty, odcd, lttime, eddt, wknote, wkcomment);
                    }
                    else
                    {
                        throw new Exception("正しい生産区分を設定してください");
                    }
                    cmd.CommandText = insertSQL;
                    int insertCount = cmd.ExecuteNonQuery();

                    // ④KD8450:切削オーダーファイルの登録（各設備毎に分解）
                    for (int mpseq = 1; mpseq <= ktsu; mpseq++)
                    {
                        // XT-XT2に変換
                        if (mcgcd[mpseq - 1] == "XT" && mccd[mpseq - 1] == "XT")
                        {
                            mccd[mpseq - 1] = "XT2";

                            if (productkbn == "3")
                            {
                                insertSQL = DivideMpOrderSQL(newOdrno, mpseq, mcgcd[mpseq - 1], mccd[mpseq - 1], hmcd, eddt, odrqty);
                            }
                            // 生産区分が「２：内示平準」の場合、内示数を４週に分割して週の初めに登録
                            else if (productkbn == "2")
                            {
                                insertSQL = DivideMpDivideOrderSQL(newOdrno, mpseq, mcgcd[mpseq - 1], mccd[mpseq - 1], hmcd, eddt, odrqty);
                                seq += 3; // ここまで来てから呼び出し元の変数値を操作
                            }
                            cmd.CommandText = insertSQL;
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                ret = true;
            }
            catch
            {
                $"手配登録に失敗しました\n{insertSQL}".ConsoleWriteLinePadded();
            }
            return ret;
        }
        /// <summary>
        /// SQL 構文編集 (KD8430 切削手配ファイル) 
        /// </summary>
        /// <returns>SQL 構文</returns>
        private static string InsertMpOrderSQL(string newOdrno, int ktseq, string hmcd, string ktcd, decimal odrqty, string odcd, int lttime
            , DateTime eddt, string wknote, string wkcomment)
        {
            wknote = string.IsNullOrEmpty(wknote) ? "null" : "'" + wknote + "'";
            wkcomment = string.IsNullOrEmpty(wkcomment) ? "null" : "'" + wkcomment + "'";
            string sql = $@"insert into kd8430 (
                ODRNO,KTSEQ,HMCD,KTCD,ODRQTY,
                ODCD,NEXTODCD,LTTIME,STDT,STTIM,
                EDDT,EDTIM,ODRSTS,QRCD,JIQTY,
                DENPYOKBN,DENPYODT,NOTE,WKNOTE,WKCOMMENT,
                DATAKBN,INSTID,INSTDT,UPDTID,UPDTDT,
                UKCD,NAIGAIKBN,RETKTCD,MPCARDDT,MPINSTID,
                MPUPDTID) values (
                '{newOdrno}',{ktseq},'{hmcd}','{ktcd}',{odrqty},
                '{odcd}',null,{lttime},null,null,
                '{eddt}','08:10','2',null,0,
                '1',null,null, {wknote} ,{wkcomment},
                '1','YAKAN','{DateTime.Now}','YAKAN','{DateTime.Now}',
                '','1','{ktcd}',null,'YAKAN',
                'YAKAN')";
            return sql;
        }
        /// <summary>
        /// SQL 構文編集 (KD8430 切削手配ファイル) 
        /// </summary>
        /// <returns>SQL 構文</returns>
        private static string InsertMpDivideOrderSQL(string newOdrno, int ktseq, string hmcd, string ktcd, decimal odrqty, string odcd, int lttime
            , DateTime eddt, string wknote, string wkcomment)
        {
            wknote = string.IsNullOrEmpty(wknote) ? "null" : "'" + wknote + "'";
            wkcomment = string.IsNullOrEmpty(wkcomment) ? "null" : "'" + wkcomment + "'";

            // 月の初めの月曜日を取得
            DateTime d = new DateTime(eddt.Year, eddt.Month, 1);
            d = d.AddDays(((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7);

            string yymm = newOdrno[..4];
            int q = int.Parse(newOdrno.Substring(4, 6));

            string sql = $@"insert into kd8430 (
                ODRNO,KTSEQ,HMCD,KTCD,ODRQTY,
                ODCD,NEXTODCD,LTTIME,STDT,STTIM,
                EDDT,EDTIM,ODRSTS,QRCD,JIQTY,
                DENPYOKBN,DENPYODT,NOTE,WKNOTE,WKCOMMENT,
                DATAKBN,INSTID,INSTDT,UPDTID,UPDTDT,
                UKCD,NAIGAIKBN,RETKTCD,MPCARDDT,MPINSTID,
                MPUPDTID) values 
                ('{yymm}{q:000000}',{ktseq},'{hmcd}','{ktcd}',{Math.Floor(odrqty / 4)},
                '{odcd}',null,{lttime},null,null,
                '{d}','08:10','2',null,0,
                '1',null,null, {wknote} ,{wkcomment},
                '1','YAKAN','{DateTime.Now}','YAKAN','{DateTime.Now}',
                '','1','{ktcd}',null,'YAKAN','YAKAN'),
                ('{yymm}{q+1:000000}',{ktseq},'{hmcd}','{ktcd}',{Math.Floor(odrqty / 4)},
                '{odcd}',null,{lttime},null,null,
                '{d.AddDays(7)}','08:10','2',null,0,
                '1',null,null, {wknote} ,{wkcomment},
                '1','YAKAN','{DateTime.Now}','YAKAN','{DateTime.Now}',
                '','1','{ktcd}',null,'YAKAN','YAKAN'),
                ('{yymm}{q+2:000000}',{ktseq},'{hmcd}','{ktcd}',{Math.Floor(odrqty / 4)},
                '{odcd}',null,{lttime},null,null,
                '{d.AddDays(14)}','08:10','2',null,0,
                '1',null,null, {wknote} ,{wkcomment},
                '1','YAKAN','{DateTime.Now}','YAKAN','{DateTime.Now}',
                '','1','{ktcd}',null,'YAKAN','YAKAN'),
                ('{yymm}{q+3:000000}',{ktseq},'{hmcd}','{ktcd}',{Math.Floor(odrqty / 4)},
                '{odcd}',null,{lttime},null,null,
                '{d.AddDays(21)}','08:10','2',null,0,
                '1',null,null, {wknote} ,{wkcomment},
                '1','YAKAN','{DateTime.Now}','YAKAN','{DateTime.Now}',
                '','1','{ktcd}',null,'YAKAN','YAKAN')
            ";
            return sql;
        }
        /// <summary>
        /// SQL 構文編集 (KD8450 切削オーダーファイル) 
        /// </summary>
        /// <returns>SQL 構文</returns>
        private static string DivideMpOrderSQL(string newOdrno, int mpseq, string mcgcd, string mccd, string hmcd, DateTime eddt, decimal odrqty)
        {
            string sql = $@"insert into kd8450 (
                ODRNO,MPSEQ,MCGCD,MCCD,HMCD,
                EDDT,ODRQTY,JIQTY,ODRSTS,MPINSTID,
                MPUPDTID) values (
                '{newOdrno}',{mpseq},'{mcgcd}','{mccd}','{hmcd}',
                '{eddt}',{odrqty},0,'2','YAKAN',
                'YAKAN')";
            return sql;
        }
        /// <summary>
        /// SQL 構文編集 (KD8450 切削オーダーファイル) 
        /// </summary>
        /// <returns>SQL 構文</returns>
        private static string DivideMpDivideOrderSQL(string newOdrno, int mpseq, string mcgcd, string mccd, string hmcd, DateTime eddt, decimal odrqty)
        {
            // 月の初めの月曜日を取得
            DateTime d = new DateTime(eddt.Year, eddt.Month, 1);
            d = d.AddDays(((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7);

            string yymm = newOdrno[..4];
            int q = int.Parse(newOdrno.Substring(4, 6));

            string sql = $@"insert into kd8450 (
                ODRNO,MPSEQ,MCGCD,MCCD,HMCD,
                EDDT,ODRQTY,JIQTY,ODRSTS,MPINSTID,
                MPUPDTID) values 
                ('{yymm}{q:000000}',{mpseq},'{mcgcd}','{mccd}','{hmcd}',
                '{d}',{Math.Floor(odrqty / 4)},0,'2','YAKAN','YAKAN'),
                ('{yymm}{q+1:000000}',{mpseq},'{mcgcd}','{mccd}','{hmcd}',
                '{d.AddDays(7)}',{Math.Floor(odrqty / 4)},0,'2','YAKAN','YAKAN'),
                ('{yymm}{q+2:000000}',{mpseq},'{mcgcd}','{mccd}','{hmcd}',
                '{d.AddDays(14)}',{Math.Floor(odrqty / 4)},0,'2','YAKAN','YAKAN'),
                ('{yymm}{q+3:000000}',{mpseq},'{mcgcd}','{mccd}','{hmcd}',
                '{d.AddDays(21)}',{Math.Floor(odrqty / 4)},0,'2','YAKAN','YAKAN')
            ";
            return sql;
        }

        // 翌稼働日の取得
        public static DateTime GetNextWorkDay(string? mpSchema, ref MySqlConnection? mpCnn, string hmcd, DateTime eddt, string productkbn)
        {
            string sql;
            if (productkbn == "2")
            {
                sql = @$"
                select min(YMD) from {mpSchema}.s0820 where caltyp='00001' and wkkbn='1' and YMD > '{eddt}'
                ";
            }
            else
            {
                sql = @$"
                select min(YMD) from {mpSchema}.s0820 where caltyp='00001' and wkkbn='1' and YMD >
                    (select max(eddt) from kd8430 where HMCD = '{hmcd}' and eddt >= '{eddt}' and ODCD = '60699')
                ";
            }
            using MySqlCommand cmd = new(sql, mpCnn);
            var result = cmd.ExecuteScalar();
            return (result != null) ? (DateTime)result : eddt;
        }


        /// <summary>
        /// 通知ファイル登録処理
        /// </summary>
        /// <returns>挿入件数</returns>
        public static int InsertKD8520(string? mpSchema, ref MySqlConnection? mpCnn, DataTable dt)
        {
            int ret = -1;
            string sql;
            if (mpCnn is null || mpSchema is null)
            {
                "MySQL 接続が確立されていません．".ConsoleWriteLinePadded();
                return ret;
            }

            // SS工程への通知メッセージ作成（福井化成）
            var sbSS = new StringBuilder();
            var targetsSS = new[] { "129H01-59560", "129H01-59570" };
            var groupsSS = dt.AsEnumerable()
                .Where(r => targetsSS.Contains(r.Field<string>("HMCD")))
                .GroupBy(r => r.Field<string>("HMCD")); // HMCD（品番）でグループ化
            foreach (var g in groupsSS)
            {
                string hmcd = (g.Key != null) ? g.Key : "";

                // 日付＋数量のリストを作成
                var items = g.Select(r =>
                {
                    DateTime judt = r.Field<DateTime>("JUDT");
                    int qty = r.Field<int>("JUQTY");

                    string md = $"{judt.Month}/{judt.Day}";
                    return $"「{md}、{qty}本」";
                }).ToList();

                // 1品番分の通知文を生成
                string line = $"「{hmcd}」{string.Join("", items)}が受注登録されました。EMを確認してください。";

                sbSS.Append(line);
            }
            string commentSS = sbSS.ToString();

            // SW工程への通知メッセージ作成（大和精工）
            var sbSW = new StringBuilder();
            var targetsSW = new[] { "R1411-07534" };
            var groupsSW = dt.AsEnumerable()
                .Where(r => targetsSW.Contains(r.Field<string>("HMCD")))
                .GroupBy(r => r.Field<string>("HMCD")); // HMCD（品番）でグループ化
            foreach (var g in groupsSW)
            {
                string hmcd = (g.Key != null) ? g.Key : "";

                // 日付＋数量のリストを作成
                var items = g.Select(r =>
                {
                    DateTime judt = r.Field<DateTime>("JUDT");
                    int qty = r.Field<int>("JUQTY");

                    string md = $"{judt.Month}/{judt.Day}";
                    return $"「{md}、{qty}本」";
                }).ToList();

                // 1品番分の通知文を生成
                string line = $"「{hmcd}」{string.Join("", items)}が受注登録されました。EMを確認してください。";

                sbSW.Append(line);
            }
            string commentSW = sbSW.ToString();

            // 通知ファイル登録
            using (MySqlTransaction txn = mpCnn.BeginTransaction())
            {
                try
                {
                    var countInsert = 0;

                    // ① SS工程の登録
                    if (!string.IsNullOrEmpty(commentSS))
                    {
                        string sqlSS = $"INSERT INTO {mpSchema}.kd8520 "
                        + $"(MCGCD, COMMENT) VALUES ('SS', '{commentSS}')";
                        using (MySqlCommand myCmd = new(sqlSS, mpCnn))
                        {
                            countInsert += myCmd.ExecuteNonQuery();
                        }
                    }

                    // ② SW工程の登録
                    if (!string.IsNullOrEmpty(commentSW))
                    {
                        string sqlSW = $"INSERT INTO {mpSchema}.kd8520 "
                        + $"(MCGCD, COMMENT) VALUES ('SW', '{commentSW}')";
                        using (MySqlCommand myCmd = new(sqlSW, mpCnn))
                        {
                            countInsert += myCmd.ExecuteNonQuery();
                        }
                    }

                    // ③トランザクションのコミット
                    txn.Commit();

                    // ④ 戻り値は挿入した件数
                    ret = countInsert;

                }
                catch (Exception ex)
                {
                    // ⑤ 異常の場合はロールバック
                    txn.Rollback();
                    "通知ファイル登録処理はロールバックしました．".ConsoleWriteLinePadded();
                    Console.WriteLine(ex.Message);
                }
            }
            return ret;
        }







    }
}
