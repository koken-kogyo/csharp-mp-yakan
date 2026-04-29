using DecryptPassword;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Data;

namespace MPYakan
{
    public static class DBManager_MySQL
    {
        /// <summary>
        /// Oracle データベース スキーマ接続成否
        /// </summary>
        /// <param name="mpCnn">MySQL データベースへの接続クラス</param>
        /// <returns>結果 (false: 失敗, true: 成功)</returns>
        public static bool IsConnectOraSchema(ref MySqlConnection? mpCnn, IConfiguration config)
        {
            bool ret;

            // appsettings.json からデータベース情報を読み込む
            string? server = config["MPSERVER"]; ;
            string? database = config["MPSCHEMA"];
            int? port = Convert.ToInt32(config["MPPORT"]);
            string? uid = config["MPUSER"];
            string charset = "utf8mb4";

            // パスワード復号化
            var dpc = new DecryptPasswordClass();
            string? encpassed = config["MPPASS"];
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
            if (mpCnn is null)
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
                        "where ODRSTS in ('1','2','3') and " +
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
                    "ロールバックしました．".ConsoleWriteLinePadded();
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
        public static bool HowManyOrders(string? mpSchema, ref MySqlConnection? mpCnn, ref DataTable calendarDt)
        {
            bool ret = false;
            try
            {
                DateTime[] fromDt = new DateTime[3];      // 0：先週、1：今週、2：来週
                DateTime[] toDt = new DateTime[3];        // 0：先週、1：今週、2：来週

                // M0340：手配先管理期間マスタから今週と来週の日付を取得
                var m0340Dt = new DataTable();
                var sql = "select ZKTSTDT as 前回確定開始日, ZKTEDDT as 前回確定終了日, KKTSTDT as 今回確定開始日, KKTEDDT as 今回確定終了日 from m0340";
                using (MySqlCommand myCmd = new(sql, mpCnn))
                {
                    using MySqlDataAdapter myDa = new(myCmd);
                    myDa.Fill(m0340Dt);
                }
                if (m0340Dt.Rows.Count == 0)
                {
                    throw new Exception("M0340：手配先管理期間マスタの読み込みで異常が発生しました");
                }
                fromDt[1] = (DateTime)m0340Dt.Rows[0]["前回確定開始日"];
                toDt[1] = (DateTime)m0340Dt.Rows[0]["前回確定終了日"];
                fromDt[2] = (DateTime)m0340Dt.Rows[0]["今回確定開始日"];
                toDt[2] = (DateTime)m0340Dt.Rows[0]["今回確定終了日"];

                // 稼働日ベースでの先週の日付をカレンダーテーブルから取得
                DateTime prevStartDay = fromDt[1];
                int workingDaysCount = 0;
                while (workingDaysCount == 0)
                {
                    prevStartDay = prevStartDay.AddDays(-7);
                    workingDaysCount = calendarDt.AsEnumerable()
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
                sql = "delete from " + mpSchema + ".kd8510 where EDDT between " +
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
                ret = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            // 接続を閉じる
            // cmn.Dbm.CloseMySqlSchema(mpCnn);
            return ret;
        }



    }
}
