using Microsoft.Extensions.Configuration;
using DecryptPassword;
using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace MPYakan
{
    public static class DBManager_Oracle
    {
        /// <summary>
        /// Oracle データベース スキーマ接続成否
        /// </summary>
        /// <param name="oraCnn">EM データベースへの接続クラス</param>
        /// <returns>結果 (false: 失敗, true: 成功)</returns>
        public static bool IsConnectOraSchema(ref OracleConnection? emCnn, Common.EmConfig emConfig)
        {
            bool ret = false;

            // appsettings.json からデータベース情報を読み込む
            string userid = emConfig.USER;
            string encpassed = emConfig.PASS;
            string host = emConfig.HOST;
            string sid = "KOKEN";

            // パスワード復号化
            var dpc = new DecryptPasswordClass();
            dpc.DecryptPassword(encpassed, out string decPasswd);

            // データソース
            string ds = "(DESCRIPTION="
                        + "(ADDRESS="
                          + "(PROTOCOL=tcp)"
                          + "(HOST=" + host + ")"
                          + "(PORT=1521)"
                        + ")"
                        + "(CONNECT_DATA="
                          + "(SERVICE_NAME=" + sid + ")"
                        + ")"
                      + ")";  // Oracle Client を使用せず直接接続する

            // Oracle 接続文字列を組み立てる
            string connectString = "User Id=" + userid + "; "
                                 + "Password=" + decPasswd + "; "
                                 + "Data Source=" + ds;
            try
            {
                emCnn = new OracleConnection(connectString);

                // Oracle へのコネクションの確立
                emCnn.Open();
                ret = true;
            }
            catch
            {
                // 接続を閉じる
                CloseOraSchema(ref emCnn);
            }
            return ret;
        }

        /// <summary>
        /// Oracle データベース スキーマからの切断
        /// </summary>
        /// <param name="emCnn">Oracle データベースへの接続クラス</param>
        public static void CloseOraSchema(ref OracleConnection? emCnn)
        {
            emCnn?.Close();
        }

        /// <summary>
        /// 注文情報データ手配状態取得
        /// </summary>
        /// <param name="emDt">注文情報データ</param>
        /// <returns>注文情報データ</returns>
        public static bool GetD0410ODRSTS(string? emSchema, ref OracleConnection? emCnn, ref DataTable emDt)
        {
            if (emCnn is null || emCnn.State != ConnectionState.Open)
            {
                "Oracle 接続が確立されていません．".ConsoleWriteLinePadded();
                return false;
            }

            if (string.IsNullOrWhiteSpace(emSchema))
            {
                "スキーマ名が設定されていません．".ConsoleWriteLinePadded();
                return false;
            }

            bool ret = false;

            string yyMM = DateTime.Now.AddMonths(-3).ToString("yyMM");
            string fromDt = DateTime.Now.AddDays(-31).ToString("yyyy/MM/dd");
            string toDt = DateTime.Now.AddDays(14).ToString("yyyy/MM/dd");
            try
            {
                string sql =
                    "SELECT ODRNO, ODRSTS, JIQTY, DENPYODT, UPDTID, UPDTDT " +
                    "FROM " + emSchema + ".D0410 " +
                    "WHERE ODRNO > :odrno " +
                    "and ODCD like '6060%' " +
                    "and ODRSTS in ('2','3','4','9') " +
                    "and EDDT between TO_DATE(:fromDt, 'YYYY/MM/DD') AND TO_DATE(:toDt, 'YYYY/MM/DD')";

                using var cmd = new OracleCommand(sql, emCnn);
                cmd.Parameters.Add(new OracleParameter("odrno", yyMM + "000000")); // EDDTにインデックスが貼ってないので検索対象をまず絞ってから抽出する
                cmd.Parameters.Add(new OracleParameter("fromDt", fromDt));
                cmd.Parameters.Add(new OracleParameter("toDt", toDt));

                using var adapter = new OracleDataAdapter(cmd);
                adapter.Fill(emDt);

                ret = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return ret;
        }


        public static bool GetYMD(string? emSchema, ref OracleConnection? emCnn, ref DataTable calendarDt, ref DataTable controlDt)
        {
            bool ret = false;
            try
            {
                // カレンダーマスタ
                string sql1 = 
                    "select YMD from " + emSchema + ".S0820 " +
                    "where CALTYP = '00001' and WKKBN = '1' " +
                    "and YMD between ADD_MONTHS(SYSDATE, -1) and ADD_MONTHS(SYSDATE, 2)";
                using OracleDataAdapter myDa1 = new(new OracleCommand(sql1, emCnn));
                {
                    myDa1.Fill(calendarDt);
                }
                // 手配先管理期間マスタ[60600]
                string sql2 = "select " +
                    "ZKTSTDT as 前回確定開始日, ZKTEDDT as 前回確定終了日, ZKTODDT as 前回確定作成日, " +
                    "KKTSTDT as 今回確定開始日, KKTEDDT as 今回確定終了日, KKTODDT as 今回確定作成日 " +
                    "from " + emSchema + ".M0340 " +
                    "where ODCTLNO='60600'";
                using OracleDataAdapter myDa2 = new(new OracleCommand(sql2, emCnn));
                {
                    myDa2.Fill(controlDt);
                }
                ret = calendarDt.Rows.Count > 0 && controlDt.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return ret;
        }

        /// <summary>
        /// 内示受注ファイルに新規登録があった品番を取得
        /// 　ループ中に１件ずつSQLを投げていたらパフォーマンスが悪かったので、
        /// 　SQL１発で集計出来る方式に変更
        /// </summary>
        /// <param name="emDt">注文情報データ</param>
        /// <returns>注文情報データ</returns>
        public static bool GetD0030Target(string? emSchema, ref OracleConnection? emCnn, ref DataTable kd8500, ref DataTable km8435, ref DataTable targetDt)
        {
            if (emCnn is null || emCnn.State != ConnectionState.Open)
            {
                "Oracle 接続が確立されていません．".ConsoleWriteLinePadded();
                return false;
            }

            // ①共通部品を展開したパーツリストを作成
            var partsList = new List<(string HmCd, string? Part, string? OyaKbn, DateTime LastDt)>();
            foreach (DataRow r in kd8500.Rows)
            {
                string hmcd = r.Field<string>("HMCD") ?? "";
                string oyakbn = r.Field<string>("OYAKBN") ?? "0";
                DateTime lastDt = r["LASTDT"] is DBNull ? new DateTime(1900, 1, 1) : (DateTime)r["LASTDT"];

                var hmcds = km8435.AsEnumerable()
                    .Where(x => x.Field<string>("HMCD") == hmcd)
                    .Select(x => x.Field<string>("HMCDS"))
                    .ToList();
                if (hmcds.Count > 0)
                {
                    foreach (var p in hmcds)
                        partsList.Add((hmcd, p, oyakbn, lastDt));
                }
                else
                {
                    partsList.Add((hmcd, hmcd, oyakbn, lastDt));
                }
            }
            var partsValue = string.Join(" UNION ALL ",
                partsList.Select(x =>
                    $"SELECT '{x.HmCd}' AS HMCD,'{x.Part}' AS PART,TO_DATE('{x.LastDt:yyyy-MM-dd HH:mm:ss}', 'YYYY-MM-DD HH24:MI:SS') AS LASTDT FROM DUAL"
                )
            );

            // ②「構成親区分が"0"」のリストを作成
            var bomValue = "select * from V_BOM_LEAF_PARENT where KOHMCD in (" +
                string.Join(",", partsList.Where(x => x.OyaKbn == "0").Select(x => $"'{x.Part}'")) + ") ";

            // ③「構成親区分が"1"」のリストを作成
            var oyaValue = string.Join(" ",
                partsList.Where(x => x.OyaKbn == "1").Select(x => $"UNION SELECT '{x.HmCd}','{x.HmCd}' FROM DUAL"));

            // ④集計SQL生成
            // 　C3001：ティエラと、C2105：ﾔﾝﾏｰ塚口は再来月の内示しかなさげ
            var sqlSamary = $@"
                -- 内示生産管理ファイルを共通部品で展開したリスト（前回登録日時が重要）
                with VPARTS as (
                    {partsValue}
                ), 
                -- 出荷品番に変換(VBOM) v.KOHMCD > v.OYAHMCD
                VBOM as
                (
                    {bomValue}{oyaValue}
                )
                select p.HMCD,max(d30.INSTDT) as LASTDT,
                    case when to_number(to_char(SYSDATE,'DD'))<13
                        then case when d30.TKCD not in ('C3001','C2105') 
                            then to_char(add_months(SYSDATE,0),'YYYY/MM')
                            else to_char(add_months(SYSDATE,0),'YYYY/MM') end
                        else case when d30.TKCD not in ('C3001','C2105') 
                            then to_char(add_months(SYSDATE,1),'YYYY/MM')
                            else to_char(add_months(SYSDATE,1),'YYYY/MM') end
                    end as TARGETYYMM, 
                    min(d30.JUDT) as JUDT,
                    sum(d30.JUQTY) as JUQTY
                from VBOM v
                    inner join VPARTS p on p.PART=v.KOHMCD
                    inner join {emSchema}.D0030 d30 on d30.HMCD=v.OYAHMCD
                where d30.JUDT between
                    case when to_number(to_char(SYSDATE,'DD'))<13
                        then case when d30.TKCD not in ('C3001','C2105') 
                            then add_months(trunc(SYSDATE,'MONTH'),0)
                            else add_months(trunc(SYSDATE,'MONTH'),0) end
                        else case when d30.TKCD not in ('C3001','C2105') 
                            then add_months(trunc(SYSDATE,'MONTH'),1)
                            else add_months(trunc(SYSDATE,'MONTH'),1) end
                    end and
                    case when to_number(to_char(SYSDATE,'DD'))<13
                        then case when d30.TKCD not in ('C3001','C2105') 
                            then last_day(add_months(SYSDATE,0))
                            else last_day(add_months(SYSDATE,0)) end
                        else case when d30.TKCD not in ('C3001','C2105')
                            then last_day(add_months(SYSDATE,1))
                            else last_day(add_months(SYSDATE,1)) end
                    end
                    and ((d30.CHK<>'43' AND d30.CHK<>'100') OR d30.CHK IS NULL)
                group by p.HMCD,
                    case when to_number(to_char(SYSDATE,'DD'))<13
                        then case when d30.TKCD not in ('C3001','C2105') 
                            then to_char(add_months(SYSDATE,0),'YYYY/MM')
                            else to_char(add_months(SYSDATE,0),'YYYY/MM') end
                        else case when d30.TKCD not in ('C3001','C2105') 
                            then to_char(add_months(SYSDATE,1),'YYYY/MM')
                            else to_char(add_months(SYSDATE,1),'YYYY/MM') end
                    end
                having 
                    max(d30.INSTDT)>max(p.LASTDT)
            ";
            targetDt.Load(new OracleCommand(sqlSamary, emCnn).ExecuteReader());

            return true;
        }

        /*
         * C3001：ティエラ、C2105：ﾔﾝﾏｰ塚口は内示の仕様が違うので調査中

                        select p.HMCD,max(d30.INSTDT) as LASTDT,
                            case when to_number(to_char(SYSDATE,'DD'))<15
                                then case when d30.TKCD not in ('C3001','C2105') 
                                    then to_char(SYSDATE,'YYYY/MM')
                                    else to_char(add_months(SYSDATE,1),'YYYY/MM') end
                                else case when d30.TKCD not in ('C3001','C2105') 
                                    then to_char(add_months(SYSDATE,1),'YYYY/MM')
                                    else to_char(add_months(SYSDATE,2),'YYYY/MM') end
                            end as TARGETYYMM, 
                            min(d30.JUDT) as JUDT,
                            sum(d30.JUQTY) as JUQTY
                        from VBOM v
                            inner join VPARTS p on p.PART=v.KOHMCD
                            inner join {emSchema}.D0030 d30 on d30.HMCD=v.OYAHMCD
                        where d30.JUDT between
                            case when to_number(to_char(SYSDATE,'DD'))<15
                                then case when d30.TKCD not in ('C3001','C2105') 
                                    then trunc(SYSDATE,'MONTH')
                                    else add_months(trunc(SYSDATE,'MONTH'),1) end
                                else case when d30.TKCD not in ('C3001','C2105') 
                                    then add_months(trunc(SYSDATE,'MONTH'),1)
                                    else add_months(trunc(SYSDATE,'MONTH'),2) end
                            end and
                            case when to_number(to_char(SYSDATE,'DD'))<15
                                then case when d30.TKCD not in ('C3001','C2105') 
                                    then last_day(SYSDATE)
                                    else last_day(add_months(SYSDATE,1)) end
                                else case when d30.TKCD not in ('C3001','C2105')
                                    then last_day(add_months(SYSDATE,1))
                                    else last_day(add_months(SYSDATE,2)) end
                            end
                            and ((d30.CHK<>'43' AND d30.CHK<>'100') OR d30.CHK IS NULL)
                        group by p.HMCD,
                            case when to_number(to_char(SYSDATE,'DD'))<15
                                then case when d30.TKCD not in ('C3001','C2105') 
                                    then to_char(SYSDATE,'YYYY/MM')
                                    else to_char(add_months(SYSDATE,1),'YYYY/MM') end
                                else case when d30.TKCD not in ('C3001','C2105') 
                                    then to_char(add_months(SYSDATE,1),'YYYY/MM')
                                    else to_char(add_months(SYSDATE,2),'YYYY/MM') end
                            end
                        having 
                            max(d30.INSTDT)>max(p.LASTDT)


         */


    }
}
