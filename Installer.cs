using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.Win32;

static class InstallEngine
{
    const string Backup="AcademiaArabicOriginalBackup";
    static void Require(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
    public static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static string Safe(string root,string relative)
    {
        Require(!String.IsNullOrEmpty(relative)&&!Path.IsPathRooted(relative)&&!relative.Contains(":"),"Invalid package path");
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(Directory.Exists(root))Require((File.GetAttributes(root)&FileAttributes.ReparsePoint)==0,"Linked game roots require manual review");
        string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        Require(full.StartsWith(root,StringComparison.OrdinalIgnoreCase),"Package path escapes its folder");
        for(string p=full;!String.IsNullOrEmpty(p)&&p.Length>=root.Length;p=Path.GetDirectoryName(p))
            if(File.Exists(p)||Directory.Exists(p))Require((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Linked game paths require manual review");
        return full;
    }
    static Stream Resource(string name){var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name);Require(stream!=null,"Missing installer resource: "+name);return stream;}
    static XDocument Manifest(){using(var s=Resource("manifest"))return XDocument.Load(s);}
    static void Copy(string src,string dest){Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(src,dest,true);}
    static bool Matches(string p,string sha){return File.Exists(p)&&Hash(p)==sha;}
    static void Closed(string game)
    {
        foreach(var p in Process.GetProcessesByName("DodgeballAcademia"))
        {
            try{Require(!String.Equals(Path.GetDirectoryName(p.MainModule.FileName),game,StringComparison.OrdinalIgnoreCase),"أغلق اللعبة ثم أعد المحاولة.");}
            catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("تعذر التأكد من إغلاق اللعبة. أغلق جميع نسخ Dodgeball Academia ثم أعد المحاولة.");}
        }
    }
    public static void Run(string action,string game,Action<string> log,int failAfter=-1)
    {
        game=Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);Require(Directory.Exists(game),"اختر مجلد اللعبة الصحيح.");
        Closed(game);var manifest=Manifest();var root=manifest.Root;
        foreach(var guard in root.Elements("Guard"))Require(Matches(Safe(game,(string)guard.Attribute("path")),(string)guard.Attribute("sha256")),"إصدار اللعبة لا يطابق النسخة المدعومة. لم تتغير الملفات.");
        string backup=Safe(game,Backup),statePath=Safe(backup,"state.xml");
        XDocument state=File.Exists(statePath)?XDocument.Load(statePath):null;
        bool installed=state!=null&&(string)state.Root.Attribute("status")=="installed";
        if(action=="verify"||(action=="install"&&installed))
        {
            Require(installed,"التعريب غير مثبت بهذه الحزمة.");VerifyState(game,backup,state);
            Require((string)state.Root.Attribute("version")== (string)root.Attribute("version"),"توجد نسخة سابقة من التعريب. اضغط إزالة واسترجاع في هذا المثبت، ثم اضغط تثبيت التعريب لتركيب النسخة الجديدة.");
            log("ملفات التعريب والنسخ الأصلية سليمة. نتيجة العرض داخل اللعبة تحتاج تجربة فعلية.");return;
        }
        Require(action=="install"||action=="restore","Unknown installer action");
        if(action=="restore"&&!installed){log("لا توجد نسخة مثبتة بهذه الحزمة لإزالتها.");return;}
        if(installed)VerifyState(game,backup,state,true);
        var work=new List<XElement>();
        if(action=="install")
        {
            foreach(var patch in root.Elements("Patch"))
            {
                string rel=(string)patch.Attribute("path"),original=(string)patch.Attribute("original");
                Require(Matches(Safe(game,rel),original),"ملف اللعبة معدل أو غير مدعوم: "+rel);
                string saved=Safe(backup,"original/"+rel);
                if(File.Exists(saved))Require(Matches(saved,original),"النسخة الاحتياطية غير سليمة: "+rel);
                work.Add(new XElement("File",new XAttribute("path",rel),new XAttribute("sha256",(string)patch.Attribute("sha256")),new XAttribute("original",original),new XAttribute("owned","true")));
            }
            foreach(var add in root.Elements("Add"))
            {
                string rel=(string)add.Attribute("path"),path=Safe(game,rel),sha=(string)add.Attribute("sha256");bool exists=File.Exists(path);
                bool mutable=(string)add.Attribute("mutable")=="true";
                bool configCompatible=mutable&&exists&&Regex.IsMatch(File.ReadAllText(path),@"(?m)^\s*UnityBaseLibrariesSource\s*=\s*$")&&Regex.IsMatch(File.ReadAllText(path),@"(?mi)^\s*UpdateInteropAssemblies\s*=\s*true\s*$");
                Require(!exists||Matches(path,sha)||configCompatible,"توجد ملفات مختلفة لمود آخر. لم تتغير الملفات: "+rel);
                var item=new XElement("File",new XAttribute("path",rel),new XAttribute("sha256",sha),new XAttribute("owned",exists?"false":"true"));
                if(mutable)item.Add(new XAttribute("mutable","true"));work.Add(item);
            }
        }
        else work=state.Root.Elements("File").Select(e=>new XElement(e)).ToList();
        string stage=Safe(game,".academia-arabic-staging-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        var written=new List<XElement>();bool rescue=false;string nativeStage=null;
        try
        {
            // Preserve all current owned targets and state BEFORE the first live replacement.
            foreach(var entry in work.Where(e=>(string)e.Attribute("owned")=="true"))
            {
                string rel=(string)entry.Attribute("path"),current=Safe(game,rel);
                if(File.Exists(current))Copy(current,Safe(stage,"before/"+rel));
            }
            if(File.Exists(statePath))Copy(statePath,Safe(stage,"before-state.xml"));
            if(action=="install")
            {
                using(var stream=Resource("payload"))using(var archive=new ZipArchive(stream,ZipArchiveMode.Read))
                    foreach(var member in archive.Entries){if(String.IsNullOrEmpty(member.Name))continue;string dest=Safe(stage,"payload/"+member.FullName);Directory.CreateDirectory(Path.GetDirectoryName(dest));member.ExtractToFile(dest,true);}
                var tool=root.Element("Tool");string executable=Safe(stage,"payload/"+(string)tool.Attribute("path"));
                Require(Matches(executable,(string)tool.Attribute("sha256")),"أداة التثبيت غير سليمة.");
                // The bundled native patch tool uses legacy file APIs. Feed it short
                // ASCII filenames in a private temp folder; managed copies handle
                // Unicode and long game paths. Never launch or inspect user saves.
                nativeStage=Safe(Path.GetTempPath(),"AcademiaArabic-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(nativeStage);Copy(executable,Safe(nativeStage,"xdelta3.exe"));
                foreach(var patch in root.Elements("Patch"))
                {
                    string rel=(string)patch.Attribute("path"),patchFile=Safe(stage,"payload/"+(string)patch.Attribute("payload")),output=Safe(stage,"after/"+rel);
                    Require(Matches(patchFile,(string)patch.Attribute("payload_sha256")),"باتش غير سليم: "+rel);Directory.CreateDirectory(Path.GetDirectoryName(output));
                    Copy(Safe(game,rel),Safe(nativeStage,"source.bin"));Copy(patchFile,Safe(nativeStage,"patch.xdelta"));
                    var psi=new ProcessStartInfo(Safe(nativeStage,"xdelta3.exe"),"-d -f -s source.bin patch.xdelta output.bin"){WorkingDirectory=nativeStage,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
                    using(var process=Process.Start(psi)){var errors=process.StandardError.ReadToEnd();process.WaitForExit();Require(process.ExitCode==0,"تعذر تجهيز الباتش: "+errors);}
                    Copy(Safe(nativeStage,"output.bin"),output);
                    Require(Matches(output,(string)patch.Attribute("sha256")),"فشل التحقق من الملف الناتج: "+rel);
                    Copy(Safe(game,rel),Safe(backup,"original/"+rel));Require(Matches(Safe(backup,"original/"+rel),(string)patch.Attribute("original")),"تعذر تأمين النسخة الأصلية.");
                }
                foreach(var add in root.Elements("Add"))
                {
                    string rel=(string)add.Attribute("path"),payload=Safe(stage,"payload/"+(string)add.Attribute("payload"));
                    Require(Matches(payload,(string)add.Attribute("sha256")),"ملف الحزمة غير سليم: "+rel);Copy(payload,Safe(stage,"after/"+rel));
                }
            }
            else foreach(var entry in work.Where(e=>e.Attribute("original")!=null))Copy(Safe(backup,"original/"+(string)entry.Attribute("path")),Safe(stage,"after/"+(string)entry.Attribute("path")));
            foreach(var entry in work.Where(e=>(string)e.Attribute("owned")=="true"))
            {
                string rel=(string)entry.Attribute("path"),dest=Safe(game,rel);
                if(action=="restore"&&(string)entry.Attribute("mutable")=="true"&&File.Exists(dest)&&!Matches(dest,(string)entry.Attribute("sha256")))
                {log("احتفظت بملف الإعدادات المعدل: "+rel);continue;}
                // Record before attempting writes; even a partially copied file is rolled back.
                written.Add(entry);
                if(action=="install"||entry.Attribute("original")!=null)Copy(Safe(stage,"after/"+rel),dest);
                else if(File.Exists(dest))File.Delete(dest);
                if(failAfter>=0&&written.Count==failAfter)throw new IOException("Injected transaction failure for local installer test");
            }
            var next=new XDocument(new XElement("State",new XAttribute("status",action=="install"?"installed":"restored"),new XAttribute("version",(string)root.Attribute("version")),work));
            if(action=="install")VerifyState(game,backup,next);
            else foreach(var entry in work.Where(e=>e.Attribute("original")!=null))Require(Matches(Safe(game,(string)entry.Attribute("path")),(string)entry.Attribute("original")),"تعذر التحقق من الاسترجاع.");
            Directory.CreateDirectory(backup);next.Save(statePath);
            log(action=="install"?"اكتمل تثبيت الملفات والتحقق منها. يلزم اختبار العرض داخل اللعبة.":"تمت استعادة ملفات اللعبة الأصلية وإزالة ملفات الحزمة المملوكة لها.");
        }
        catch
        {
            try
            {
                foreach(var entry in written.AsEnumerable().Reverse())
                {
                    string rel=(string)entry.Attribute("path"),before=Safe(stage,"before/"+rel),dest=Safe(game,rel);
                    if(File.Exists(before))Copy(before,dest);else if(File.Exists(dest))File.Delete(dest);
                }
                if(File.Exists(Safe(stage,"before-state.xml")))Copy(Safe(stage,"before-state.xml"),statePath);else if(File.Exists(statePath))File.Delete(statePath);
                log("تم التراجع عن العملية وإرجاع الملفات السابقة.");
            }
            catch{rescue=true;log("تعذر التراجع بالكامل. ملفات الإنقاذ محفوظة في: "+stage);throw;}
            throw;
        }
        finally
        {
            if(nativeStage!=null)
            {
                Require(Path.GetFullPath(nativeStage).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar+"AcademiaArabic-",StringComparison.OrdinalIgnoreCase),"Unsafe native temp cleanup path");
                try{Directory.Delete(nativeStage,true);}catch{log("بقي مجلد باتش مؤقت: "+nativeStage);}
            }
            if(!rescue)
            {
                // stage is generated by Safe and checked again before recursive cleanup.
                Require(Path.GetFullPath(stage).StartsWith(game+Path.DirectorySeparatorChar+".academia-arabic-staging-",StringComparison.OrdinalIgnoreCase),"Unsafe cleanup path");
                try{Directory.Delete(stage,true);}catch{log("بقي مجلد تجهيز مؤقت: "+stage);}
            }
        }
    }
    static void VerifyState(string game,string backup,XDocument state,bool restoring=false)
    {
        foreach(var entry in state.Root.Elements("File"))
        {
            string rel=(string)entry.Attribute("path"),path=Safe(game,rel);
            bool skip=(string)entry.Attribute("mutable")=="true"||(restoring&&entry.Attribute("original")==null&&((string)entry.Attribute("owned")=="false"||!File.Exists(path)));
            if(!skip)Require(Matches(path,(string)entry.Attribute("sha256")),"ملف مثبت مفقود أو تغير؛ لم تتم إزالته: "+rel);
            if(entry.Attribute("original")!=null)Require(Matches(Safe(backup,"original/"+rel),(string)entry.Attribute("original")),"النسخة الأصلية غير سليمة: "+rel);
        }
    }
    public static List<string> FindGames()
    {
        var roots=new List<string>();
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            var steam=key==null?null:key.GetValue("SteamPath") as string;
            if(!String.IsNullOrEmpty(steam))
            {
                roots.Add(Path.Combine(steam,"steamapps","common","DodgeballAcademia"));
                string vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
                if(File.Exists(vdf))foreach(Match match in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s+\"([^\"]+)\""))roots.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\",@"\"),"steamapps","common","DodgeballAcademia"));
            }
        }
        return roots.Where(p=>File.Exists(Path.Combine(p,"DodgeballAcademia.exe"))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

class InstallerWindow:Form
{
    TextBox path=new TextBox(),output=new TextBox();Label status=new Label();Button[] actions;bool busy;
    public InstallerWindow()
    {
        Text="Dodgeball Academia Arabic — INTERNAL Candidate";Size=new Size(800,540);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",11);RightToLeft=RightToLeft.Yes;RightToLeftLayout=true;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),RowCount=5,ColumnCount=1};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,75));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text="تعريب Dodgeball Academia — نسخة اختبار داخلية\nاختر مجلد اللعبة وأغلقها قبل التثبيت. ليست حزمة إطلاق عامة بعد."});
        path.Dock=DockStyle.Fill;path.RightToLeft=RightToLeft.No;layout.Controls.Add(path);
        var row=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var browse=new Button{Text="اختيار المجلد",AutoSize=true};var install=new Button{Text="تثبيت التعريب",AutoSize=true};var verify=new Button{Text="فحص الملفات",AutoSize=true};var restore=new Button{Text="إزالة واسترجاع",AutoSize=true};actions=new[]{browse,install,verify,restore};foreach(var b in actions)row.Controls.Add(b);layout.Controls.Add(row);
        status.Text="جاهز";status.Dock=DockStyle.Fill;layout.Controls.Add(status);output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.Dock=DockStyle.Fill;layout.Controls.Add(output);Controls.Add(layout);
        try{var games=InstallEngine.FindGames();if(games.Count==1)path.Text=games[0];}catch{}
        browse.Click+=(s,e)=>{using(var dialog=new OpenFileDialog{Title="اختر ملف اللعبة",Filter="Dodgeball Academia|DodgeballAcademia.exe"})if(dialog.ShowDialog(this)==DialogResult.OK)path.Text=Path.GetDirectoryName(dialog.FileName);};
        install.Click+=async(s,e)=>await Work("install");verify.Click+=async(s,e)=>await Work("verify");restore.Click+=async(s,e)=>{if(MessageBox.Show(this,"إزالة التعريب واسترجاع ملفات اللعبة الأصلية؟","الاسترجاع",MessageBoxButtons.YesNo)==DialogResult.Yes)await Work("restore");};
        FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
    }
    void Log(string text){if(InvokeRequired){Invoke(new Action<string>(Log),text);return;}output.AppendText(text+Environment.NewLine);}
    async Task Work(string action)
    {
        string chosen=path.Text;if(String.IsNullOrWhiteSpace(chosen)){MessageBox.Show(this,"اختر مجلد اللعبة أولًا.");return;}
        busy=true;foreach(var b in actions)b.Enabled=false;path.Enabled=false;status.Text="جارٍ العمل…";
        try{await Task.Run(()=>InstallEngine.Run(action,chosen,Log));status.Text="اكتملت العملية";}
        catch(Exception error){Log(error.ToString());status.Text="لم تكتمل العملية";MessageBox.Show(this,error is UnauthorizedAccessException?"تعذر الوصول إلى المجلد. شغّل المثبت بصلاحية مناسبة ثم أعد المحاولة.":error.Message,"تعذر إكمال العملية");}
        finally{busy=false;foreach(var b in actions)b.Enabled=true;path.Enabled=true;}
    }
}
static class Program
{
    [STAThread]static int Main(string[] args)
    {
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
        AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
        if(args.Length>=4&&args[0]=="--test")
        {try{InstallEngine.Run(args[1],args[2],s=>File.AppendAllText(args[3],s+Environment.NewLine,Encoding.UTF8),args.Length>4?Int32.Parse(args[4]):-1);return 0;}catch(Exception error){File.AppendAllText(args[3],error.ToString(),Encoding.UTF8);return 1;}}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new InstallerWindow());return 0;
    }
}
