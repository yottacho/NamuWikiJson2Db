using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Npgsql;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WikiBulkInsert
{
    public partial class Form1 : Form
    {
        NpgsqlConnection connection = null;
        string initialDir = @".";
        List<NamuWiki> Data = null;

        public Form1()
        {
            InitializeComponent();
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder()
            {
                Host = txtHost.Text,
                Port = int.Parse(txtPort.Text),
                Username = txtId.Text,
                Password = txtPw.Text,
                Database = txtDb.Text,
            };

            Debug.WriteLine(builder.ConnectionString);
            if (connection == null)
            {
                connection = new NpgsqlConnection(builder.ConnectionString);
                try
                {
                    connection.Open();
                    connection.ChangeDatabase(txtDb.Text);
                }
                catch (Exception ex)
                {
                    string exMsg = ex.Message;
                    if (ex.InnerException != null)
                    {
                        exMsg += "\r\n" + ex.InnerException.Message;
                    }

                    MessageBox.Show(exMsg, "DB error");
                    connection = null;
                    return;
                }

                txtHost.Enabled = false;
                txtPort.Enabled = false;
                txtId.Enabled = false;
                txtPw.Enabled = false;
                txtDb.Enabled = false;
                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (connection != null)
            {
                try
                {
                    connection.Close();
                }
                catch { }
                connection = null;
            }
        }


        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            if (connection != null)
            {
                try
                {
                    connection.Close();
                }
                catch { }
                connection = null;
            }

            txtHost.Enabled = true;
            txtPort.Enabled = true;
            txtId.Enabled = true;
            txtPw.Enabled = true;
            txtDb.Enabled = true;
            btnConnect.Enabled = true;
            btnDisconnect.Enabled = false;
        }

        private void btnTxtFileBrowse_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.InitialDirectory = initialDir;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                txtFilePath.Text = dialog.FileName;

                FileInfo fileInfo = new FileInfo(txtFilePath.Text);
                string fname = fileInfo.Name;
                if (fname.Contains('.'))
                    fname = fname.Substring(0, fname.IndexOf('.'));
                txtTableNameBase.Text = fname.ToLower();
            }
        }

        private void btnFileCheck_Click(object sender, EventArgs e)
        {
            if (!File.Exists(txtFilePath.Text))
            {
                MessageBox.Show("File not found");
                return;
            }

            FileInfo fileInfo = new FileInfo(txtFilePath.Text);
            string fname = fileInfo.Name;
            if (fname.Contains('.'))
                fname = fname.Substring(0, fname.IndexOf('.'));
            txtTableNameBase.Text = fname.ToLower();

            btnFileCheck.Enabled = false;
            progress.Style = ProgressBarStyle.Marquee;
            progress.Value = 100;

            Data = null;

            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += (sender, e) =>
            {
                using var reader = File.OpenText(txtFilePath.Text);
                using var jsonReader = new JsonTextReader(reader);
                var processor = JsonSerializer.Create();

                try
                {
                    Data = processor.Deserialize<List<NamuWiki>>(jsonReader);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Json error");
                    Data = null;
                }
            };

            bw.RunWorkerCompleted += (sender, e) =>
            {
                btnFileCheck.Enabled = true;
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = 0;
                txtMessage.Text = $"Data loaded. {Data.Count}.";
            };
            bw.RunWorkerAsync();
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            if (connection == null)
            {
                MessageBox.Show("Database not connected!");
                return;
            }

            if (txtTableNameBase.Text == "")
            {
                MessageBox.Show("Input table name");
                return;
            }

            if (Data == null)
            {
                MessageBox.Show("Data not loaded!");
                return;
            }

            btnImport.Enabled = false;

            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.DoWork += Bw_DoWork;
            bw.ProgressChanged += Bw_ProgressChanged;
            bw.RunWorkerCompleted += Bw_RunWorkerCompleted;
            bw.RunWorkerAsync();
        }

        int currentIndex = 0;
        private void Bw_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker bw = sender as BackgroundWorker;

            string dropSql = $"DROP TABLE IF EXISTS {txtTableNameBase.Text} CASCADE";
            string createSql = $"CREATE TABLE IF NOT EXISTS {txtTableNameBase.Text} (id integer generated by default as identity, namespace varchar, title varchar, text text, contributors varchar[])";
            //string insertSql = $"INSERT INTO {txtTableNameBase.Text} (id, namespace, title, text, contributors) VALUES (default, @namespace, @title, @text, @contributors) RETURNING id";
            string insertSql = $"INSERT INTO {txtTableNameBase.Text} (id, namespace, title, text, contributors) VALUES (default, @namespace, @title, @text, @contributors)";

            try
            {
                Debug.WriteLine("Drop table ...");
                using (NpgsqlCommand dropCmd = new NpgsqlCommand(dropSql, connection))
                {
                    dropCmd.ExecuteNonQuery();
                }
                Debug.WriteLine("Create table ...");
                using (NpgsqlCommand createCmd = new NpgsqlCommand(createSql, connection))
                {
                    createCmd.ExecuteNonQuery();
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "DDL error!");
                return;
            }

            NpgsqlTransaction tran = connection.BeginTransaction();

            NamuWiki current = null;
            try
            {

                Debug.WriteLine("Start insert ...");
                int befProgress = -1;
                using (NpgsqlCommand insertCmd = new NpgsqlCommand(insertSql, connection, tran))
                {
                    insertCmd.Parameters.Clear();
                    insertCmd.Parameters.Add("namespace", NpgsqlTypes.NpgsqlDbType.Varchar);
                    insertCmd.Parameters.Add("title", NpgsqlTypes.NpgsqlDbType.Varchar);
                    insertCmd.Parameters.Add("text", NpgsqlTypes.NpgsqlDbType.Text);
                    insertCmd.Parameters.Add("contributors", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar);
                    insertCmd.Prepare();

                    for (int i = 0; i < Data.Count; i++)
                    {
                        current = Data[i];
                        currentIndex = i + 1;

                        // postgresql does not supports 0x00 in text or char, varchar
                        if (current.Namespace.Contains("\0"))
                            current.Namespace = current.Namespace.Replace('\0', ' ');

                        if (current.Title.Contains("\0"))
                            current.Title = current.Title.Replace('\0', ' ');

                        if (current.Text.Contains("\0"))
                            current.Text = current.Text.Replace('\0', ' ');

                        if (current.Contributors != null)
                        {
                            for (int t = 0; t < current.Contributors.Count; t++)
                            {
                                current.Contributors[t] = current.Contributors[t].Replace('\0', ' ');
                            }
                        }

                        insertCmd.Parameters["namespace"].Value = current.Namespace;
                        insertCmd.Parameters["title"].Value = current.Title;
                        insertCmd.Parameters["text"].Value = current.Text;
                        insertCmd.Parameters["contributors"].Value = current.Contributors;

                        insertCmd.ExecuteNonQuery();
                        //var insertedId = cmd1.ExecuteScalar();
                        //Debug.WriteLine($"> insert {insertedId}");

                        int curProgress = (int)(100.0d * ((double)i / Data.Count)) + 1;
                        if (curProgress != befProgress || i % 3000 == 0)
                        {
                            befProgress = curProgress;
                            bw.ReportProgress(curProgress);

                            tran.Commit();
                            tran.Dispose();
                            tran = connection.BeginTransaction();
                        }
                    }
                }
                tran.Commit();
            }
            catch (Exception ex)
            {
                tran.Rollback();
                Debug.WriteLine("----------------------------------------");
                Debug.WriteLine("Rollback!");
                Debug.WriteLine("Error> " + ex.Message);
                if (ex.InnerException != null)
                    Debug.WriteLine("Detail> " + ex.InnerException.Message);

                if (current != null)
                {
                    Debug.WriteLine("Data >>>>>>>>>>");
                    Debug.WriteLine($"Namespace: {current.Namespace}");
                    Debug.WriteLine($"Title: [{current.Title}]");
                    Debug.WriteLine($"Text: [{current.Text}]");
                }

                MessageBox.Show(ex.Message, "Error!");
            }
            finally
            {
                //tran.Commit();
                tran.Dispose();
            }
        }

        private void Bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progress.Value = e.ProgressPercentage;
            txtMessage.Text = $"{e.ProgressPercentage}% {currentIndex}/{Data.Count}...";
        }

        private void Bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            progress.Value = progress.Maximum;
            txtMessage.Text = $"Work end. {currentIndex} / {Data.Count}";

            Debug.WriteLine(txtMessage.Text);

            btnImport.Enabled = true;
        }
    }
}
