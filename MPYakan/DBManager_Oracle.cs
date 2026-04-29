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
                    "ZKTSTDT as 前回確定開始日, ZKTEDDT as 前回確定終了日, " +
                    "KKTSTDT as 今回確定開始日, KKTEDDT as 今回確定終了日 " +
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



    }
}
