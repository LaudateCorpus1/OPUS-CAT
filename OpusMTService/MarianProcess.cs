﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.Text.RegularExpressions;
using Serilog;
using System.Data.SQLite;
using System.Data;
using System.Windows.Controls.Primitives;

namespace FiskmoMTEngine
{
    public class MarianProcess
    {
        /*private Process tokenizeAndTruecaseProcess;
        private Process bpeProcess;
        private Process decoderProcess;*/
        private Process mtPipe;
        private string langpair;

        public string SourceCode { get; }
        public string TargetCode { get; }
        public bool Faulted { get; private set; }
        public Process MtPipe { get => mtPipe; set => mtPipe = value; }

        private StreamWriter utf8Writer;
        private string modelDir;

        public string SystemName { get; }

        private string mtPipeCmds;
        private bool sentencePiecePostProcess;
        private static readonly Object lockObj = new Object();

        //This seems like the only way to cleanly close the NMT window when Studio is closing.
        //On some systems the unclean closing was causing an error pop-up, that's why this was added.
        //On my dev system the was no pop-up, but the error was reported in Event Viewer.
        private static void KillProcessAndChildren(int pid)
        {
            // Cannot close 'system idle process'.
            if (pid == 0)
            {
                return;
            }
            ManagementObjectSearcher searcher = new ManagementObjectSearcher
                    ("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection moc = searcher.Get();
            foreach (ManagementObject mo in moc)
            {
                KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
            }
            try
            {
                Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }

        public MarianProcess(string modelDir, string sourceCode, string targetCode)
        {
            this.Faulted = false;
            this.langpair = $"{sourceCode}-{targetCode}";
            this.SourceCode = sourceCode;
            this.TargetCode = targetCode;
            this.modelDir = modelDir;
            this.SystemName = $"{sourceCode}-{targetCode}_" + (new DirectoryInfo(this.modelDir)).Name;
            
            Log.Information($"Starting MT pipe for model {this.SystemName}.");
            //Both moses+BPE and sentencepiece preprocessing are supported, check which one model is using
            if (Directory.GetFiles(this.modelDir).Any(x=> new FileInfo(x).Name == "source.spm"))
            {
                this.mtPipeCmds = "StartSentencePieceMtPipe.bat";
                this.sentencePiecePostProcess = true;
            }
            else
            {
                this.mtPipeCmds = "StartMosesBpeMtPipe.bat";
                this.sentencePiecePostProcess = false;
            }

            this.MtPipe = this.StartProcessWithCmd(this.mtPipeCmds, this.modelDir);

            this.utf8Writer = new StreamWriter(this.MtPipe.StandardInput.BaseStream, new UTF8Encoding(false));

            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            this.ShutdownMtPipe();
        }

        private Process StartProcessWithCmd(string fileName, string args)
        {
            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Process ExternalProcess = new Process();

            ExternalProcess.StartInfo.FileName = "cmd";
            ExternalProcess.StartInfo.Arguments = $"/c {fileName} {args}";
            ExternalProcess.StartInfo.UseShellExecute = false;
            //ExternalProcess.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;

            ExternalProcess.StartInfo.WorkingDirectory = pluginDir;
            ExternalProcess.StartInfo.RedirectStandardInput = true;
            ExternalProcess.StartInfo.RedirectStandardOutput = true;
            ExternalProcess.StartInfo.RedirectStandardError = true;
            ExternalProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;

            ExternalProcess.ErrorDataReceived += errorDataHandler;
            
            ExternalProcess.StartInfo.CreateNoWindow = true;
            //ExternalProcess.StartInfo.CreateNoWindow = false;
            
            ExternalProcess.Start();
            ExternalProcess.BeginErrorReadLine();

            ExternalProcess.StandardInput.AutoFlush = true;

            return ExternalProcess;
        }

        internal List<string> BatchTranslate(List<string> input)
        {
            throw new NotImplementedException();
        }

        private Process StartProcessWithRedirects(string fileName, string args)
        {
            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Process ExternalProcess = new Process();
 
            ExternalProcess.StartInfo.FileName = Path.Combine(pluginDir, fileName);
            ExternalProcess.StartInfo.Arguments = args;
            ExternalProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            ExternalProcess.StartInfo.UseShellExecute = false;

            ExternalProcess.StartInfo.WorkingDirectory = pluginDir;
            ExternalProcess.StartInfo.RedirectStandardInput = true;
            ExternalProcess.StartInfo.RedirectStandardOutput = true;
            ExternalProcess.StartInfo.RedirectStandardError = true;

            ExternalProcess.Start();
            
            return ExternalProcess;
        }

        private void errorDataHandler(object sender, DataReceivedEventArgs e)
        {
            Log.Information(e.Data);
        }

        public void ShutdownMtPipe()
        {
            KillProcessAndChildren(this.mtPipe.Id);
            //Remove the event handler so it doesn't try to kill an already killed process
            AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
        }
        
        private string TranslateSentence(string rawSourceSentence)
        {
            //This preprocessing must correspond to the one used in model training. Currently
            //this is:
            //${TOKENIZER}/replace-unicode-punctuation.perl |
            //${ TOKENIZER}/ remove - non - printing - char.perl |
            //${ TOKENIZER}/ normalize - punctuation.perl - l $1 |
            //sed 's/  */ /g;s/^ *//g;s/ *$$//g' |
            var sourceSentence = MosesPreprocessor.RunMosesPreprocessing(rawSourceSentence,this.TargetCode);
            sourceSentence = MosesPreprocessor.PreprocessSpaces(sourceSentence);
            
            this.utf8Writer.WriteLine(sourceSentence);
            this.utf8Writer.Flush();
            //this.MtPipe.StandardInput.BaseStream.Flush();
            //This inputs UTF16 by default, but models expect utf8
            //this.MtPipe.StandardInput.WriteLine(sourceSentence);
            //this.MtPipe.StandardInput.Flush();

            //There should only ever be a single line in the stdout, since there's only one line of
            //input per stdout readline, and marian decoder will never insert line breaks into translations.
            string translation = this.MtPipe.StandardOutput.ReadLine();

            if (this.sentencePiecePostProcess)
            {
                translation = (translation.Replace(" ", "")).Replace("▁", " ").Trim();
            }

            return translation;
        }

        private string FetchTranslationFromDb(string sourceText)
        {
            var translationDb = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FiskmoMTEngineSettings.Default.LocalFiskmoDir,
                FiskmoMTEngineSettings.Default.TranslationDBName);

            List<string> items = new List<string>();

            using (var m_dbConnection = new SQLiteConnection($"Data Source={translationDb};Version=3;"))
            {
                m_dbConnection.Open();

                using (SQLiteCommand fetch =
                    new SQLiteCommand("SELECT DISTINCT translation FROM translations WHERE sourcetext=@sourcetext AND model=@model LIMIT 1", m_dbConnection))
                {
                    fetch.Parameters.Add(new SQLiteParameter("@sourcetext",sourceText));
                    fetch.Parameters.Add(new SQLiteParameter("@model", this.SystemName));
                    SQLiteDataReader r = fetch.ExecuteReader();

                    while (r.Read())
                    {
                        items.Add(Convert.ToString(r["translation"]));
                    }
                }

            }

            return items.SingleOrDefault();
        }

        public string Translate(string sourceText)
        {
            
            if (this.MtPipe.HasExited)
            {
                if (this.modelDir == null)
                {
                    throw new Exception($"No local OPUS model exists for language pair {this.langpair}. Open the Settings dialog of Fiskmö translation provider to download the latest model.");
                }
                else
                {
                    throw new Exception("Opus MT functionality has stopped working. Restarting the OPUS MT service may resolve the problem.");
                }
            }

            string existingTranslation = this.FetchTranslationFromDb(sourceText);
            if (existingTranslation != null)
            {
                return existingTranslation;
            }
            else
            {
                lock (MarianProcess.lockObj)
                {
                    /*Stopwatch sw = new Stopwatch();
                    sw.Start();*/
                    //Check again if the translation has been produced during the lock
                    //waiting period
                    existingTranslation = this.FetchTranslationFromDb(sourceText);
                    if (existingTranslation != null)
                    {
                        return existingTranslation;
                    }

                    //It might be the case that the source text contains multiple sentences,
                    //potentially even line breaks, so the text needs to be split on line breaks.
                    //(sentence splitting might be nice, but having multiple sentences on one line
                    //doesn't break anything, while multiple lines cause desyncing problems.
                    var splitSource = new List<string> { sourceText };// sourceText.Split(new[] {"\r\n","\r","\n"},StringSplitOptions.None);

                    StringBuilder translationBuilder = new StringBuilder();
                    foreach (var sourceSentence in splitSource)
                    {
                        translationBuilder.Append(this.TranslateSentence(sourceSentence));
                    }

                    var translation = translationBuilder.ToString();
                
                    this.WriteTranslationToDb(sourceText, translation);
                    //sw.Stop();

                    return translation;
                }
            }
            
        }

        private void WriteTranslationToDb(string sourceText, string translation)
        {
            var translationDb = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FiskmoMTEngineSettings.Default.LocalFiskmoDir,
                FiskmoMTEngineSettings.Default.TranslationDBName);

            using (var m_dbConnection = new SQLiteConnection($"Data Source={translationDb};Version=3;"))
            {
                m_dbConnection.Open();

                using (SQLiteCommand insert =
                    new SQLiteCommand("INSERT INTO translations (sourcetext, translation, model) VALUES (@sourcetext,@translation,@model)", m_dbConnection))
                {
                    insert.Parameters.Add(new SQLiteParameter("@sourcetext", sourceText));
                    insert.Parameters.Add(new SQLiteParameter("@translation", translation));
                    insert.Parameters.Add(new SQLiteParameter("@model", this.SystemName));
                    insert.ExecuteNonQuery();
                }
            }
        }
    }

}
    