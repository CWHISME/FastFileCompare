using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// **********************************************************
///用于文件比较，核心类
///Made By Wangjiaying
///2020.7.1
///**********************************************************
/// </summary>
public class FileCompare
{
    /// <summary>
    /// 用于存储项目设置相关信息的位置
    /// </summary>
    private string _infoSaverPath;

    /// <summary>
    /// 保存设置文件路径
    /// </summary>
    private string SaveInfoFIle { get { return Path.Combine(_infoSaverPath, "FileCompreInfo.sav"); } }

    private InfoSetting _info;

    /// <summary>
    /// 设置项
    /// </summary>
    public InfoSetting Info { get { return _info; } }

    private const string ErrorMark = "ErrorMarkSuffix_";

    /// <summary>
    /// 默认存储设置为项目路径
    /// </summary>
    public FileCompare()
    {
        Init(Application.dataPath + "/..");
    }

    /// <summary>
    /// 传入自定义存储设置所在路径
    /// </summary>
    /// <param name="infoSaverPath"></param>
    public FileCompare(string infoSaverPath)
    {
        Init(infoSaverPath);
    }

    /// <summary>
    /// 初始化，构造函数自动调用
    /// </summary>
    /// <param name="infoSaverPath"></param>
    private void Init(string infoSaverPath)
    {
        _infoSaverPath = infoSaverPath;
        if (File.Exists(SaveInfoFIle))
        {
            _info = JsonUtility.FromJson<InfoSetting>(File.ReadAllText(SaveInfoFIle));
        }
        else
        {
            //不存在存储设置，说明可能为第一次打开，进行初始化
            _info = new InfoSetting();
            //初始化对比目录：我们假设始终为项目的 StreamingAssets
            _info.RightComprePath = Application.streamingAssetsPath;
        }
    }

    //====================Public DO==============================
    /// <summary>
    /// 进行一次比较
    /// </summary>
    /// <param name="onStep">每一个文件夹比较完成后，当前进度/最大进度</param>
    /// <param name="onEnd">执行完毕</param>
    public Thread Compare(Action<int, int> onStep, Action<bool, List<string>> onEnd, bool SortByDate = false)
    {
        if (!Directory.Exists(Info.LeftComprePath) || !Directory.Exists(Info.RightComprePath))
        {
            if (onEnd != null) onEnd.Invoke(false, new List<string>() { "路径错误！" });
            return null;
        }
        System.Threading.ThreadStart start = new System.Threading.ThreadStart(() =>
        {
            try
            {
                CompareByMode(Info.CompareMode, onStep, onEnd, SortByDate);
            }
            catch (Exception ex)
            {
                if (onEnd != null) onEnd.Invoke(false, new List<string>() { ex.Message + ex.InnerException + ex.StackTrace });
                return;
            }
        });
        Thread thread = new Thread(start);
        thread.Start();
        return thread;
    }

    /// <summary>
    /// 保存当前设置
    /// </summary>
    public void SaveSetting()
    {
        File.WriteAllText(SaveInfoFIle, JsonUtility.ToJson(_info));
    }

    /// <summary>
    /// 按照日期排序
    /// </summary>
    /// <param name="pathList"></param>
    public void SortPathByDate(List<string> pathList)
    {
        pathList.Sort((x, y) =>
        {
            DateTime xT = /*Directory.Exists(x) ? Directory.GetLastWriteTime(x) :*/ File.GetLastWriteTime(x);
            DateTime yT = /*Directory.Exists(y) ? Directory.GetLastWriteTime(y) :*/ File.GetLastWriteTime(y);
            return (int)((yT - xT).TotalSeconds);
        });
    }

    /// <summary>
    /// 创建补丁
    /// </summary>
    /// <param name="path">差异化文件路径</param>
    /// <param name="suffix">附加后缀</param>
    /// <param name="compress">是否压缩</param>
    /// <param name="onStep"></param>
    /// <param name="onEnd"></param>
    public void CreatePatch(List<string> path, string suffix, bool compress = false, Action<int, int> onStep = null, Action<string> onEnd = null)
    {
        if (string.IsNullOrEmpty(Info.DiffPatchPath))
        {
            if (onEnd != null) onEnd.Invoke("");
            return;
        };
        string targetPath = Path.Combine(Info.DiffPatchPath, string.Format("{0}_{1}_{2}_[{3}]", Application.platform, Info.LastVersion, DateTime.Now.ToString("yyyyMMdd-hhmm-ss"), suffix));
        if (Directory.Exists(targetPath))
            Directory.Delete(targetPath, true);
        Directory.CreateDirectory(targetPath);
        Thread thread = new Thread(new ThreadStart(() =>
        {
            for (int i = 0; i < path.Count; i++)
            {
                if (onStep != null) onStep.Invoke(i, path.Count);
                Copy(path[i], GetLeftPathByRightPath(targetPath, Info.RightComprePath, path[i]));
            }
            if (compress)
            {
                ICSharpCode.SharpZipLib.Zip.FastZip zip = new ICSharpCode.SharpZipLib.Zip.FastZip();
                zip.CreateZip(targetPath + ".zip", targetPath, true, "");
                Directory.Delete(targetPath, true);
                if (onEnd != null) onEnd.Invoke(Path.GetDirectoryName(targetPath));
            }
            else if (onEnd != null) onEnd.Invoke(targetPath);
        }));
        thread.Start();
    }

    public void Copy(string from, string to)
    {
        //是个目录
        if (Directory.Exists(from))
        {
            if (!Directory.Exists(to))
                Directory.CreateDirectory(to);
            string[] files = Directory.GetFiles(from, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }
            string[] dirs = Directory.GetDirectories(from, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in dirs)
            {
                Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }
        else if (!Info.CheckSuffix(from))
        {
            if (!Directory.Exists(Path.GetDirectoryName(to)))
                Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }
    }
    //=====================================================

    //====================Private=============================

    private void CompareByMode(ECompareMode mode, Action<int, int> onStep, Action<bool, List<string>> onEnd, bool SortByDate = false)
    {
        int totalProcessCount = 0, curProcessCount = 0;
        List<string> diffFileList = new List<string>();
        if (mode == ECompareMode.Super_Ex)
        {
            string[] dirs = Directory.GetDirectories(_info.RightComprePath, "*", SearchOption.AllDirectories);
            totalProcessCount = dirs.Length + 1;
            onStep.Invoke(curProcessCount, totalProcessCount);
            int workCount = 0;
            //比较主目录
            CompareDirectory(_info.LeftComprePath, _info.RightComprePath, diffFileList, onStep, totalProcessCount, ref curProcessCount, false);
            //比较子目录
            List<Thread> threadList = new List<Thread>();
            for (int i = 0; i < dirs.Length; i++)
            {
                string path = dirs[i];
                while (workCount > Info.CompareThreadLimit)
                {
                    Thread.Sleep(500);
                }
                //获取所对比的源目录路径
                string leftFilePath = GetLeftPathByRightPath(_info.LeftComprePath, _info.RightComprePath, path);
                //该操作是为了与正常比较模式保持一致，且优化部分性能
                if (Directory.Exists(Path.GetDirectoryName(leftFilePath)))
                    if (!Directory.Exists(leftFilePath))
                        lock (this)
                        {
                            diffFileList.Add(path);
                        }
                    else
                    {
                        workCount++;
                        Thread thread = new Thread(x =>
                        {
                            //比较文件
                            CompareDirectory(leftFilePath, path, diffFileList, onStep, totalProcessCount, ref curProcessCount, false);
                            workCount--;
                        });
                        threadList.Add(thread);
                        thread.Start();
                    }
            }
            foreach (var item in threadList)
                item.Join();
            //校验
            for (int i = 0; i < diffFileList.Count; i++)
            {
                if (!Directory.Exists(diffFileList[i]) && !File.Exists(diffFileList[i]))
                {
                    diffFileList.Insert(0, ErrorMark + "出现错误路径！Index: " + i);
                    break;
                }
            }
        }
        else
        {
            if (onStep != null && Info.ShowProgress)
            {
                //用于执行统计进度
                totalProcessCount = Directory.GetDirectories(_info.RightComprePath, "*", SearchOption.AllDirectories).Length + 1;
                onStep.Invoke(curProcessCount, totalProcessCount);
            }
            CompareDirectory(_info.LeftComprePath, _info.RightComprePath, diffFileList, onStep, totalProcessCount, ref curProcessCount, true);
        }
        bool isSuccess = diffFileList.Count > 0 ? (diffFileList[0].StartsWith(ErrorMark) ? false : true) : true;
        if (isSuccess && SortByDate)
            SortPathByDate(diffFileList);

        if (onEnd != null) onEnd.Invoke(isSuccess, diffFileList);
    }

    private void CompareDirectory(string left, string right, List<string> diffFileList, Action<int, int> onStep, int totalProcessCount, ref int curProcessCount, bool recursion)
    {
        string[] rightFiles = Directory.GetFiles(right, "*", SearchOption.TopDirectoryOnly);

        int workCount = 0;
        List<Thread> threadList = new List<Thread>();
        List<string> tempDiffList = new List<string>();
        //比对文件
        foreach (var rightFile in rightFiles)
        {
            //后缀被排除，回退
            if (Info.CheckSuffix(rightFile)) continue;
            //获取所对比的源文件路径
            string leftFilePath = GetLeftPathByRightPath(left, right, rightFile);
            //不存在，说明为新增，直接添加至列表
            if (!File.Exists(leftFilePath))
            {
                lock (this)
                {
                    diffFileList.Add(rightFile);
                }
                continue;
            }
            //日期对比
            if (Info.IsCompareFileDate && File.GetLastWriteTime(rightFile) == File.GetLastWriteTime(leftFilePath))
                continue;
            workCount++;
            Thread thread = new Thread(x =>
            {
                CompareSingleFile(leftFilePath, rightFile, tempDiffList);
                //threadCheckList[index] = true;
                workCount--;
            });
            thread.IsBackground = true;
            threadList.Add(thread);
            thread.Start();
        }

        foreach (var item in threadList)
            item.Join();
        //while (workCount > 0)
        //{
        //    //if (threadCheckList.FindAll(x => !x).Count < 1) break;
        //    Thread.Sleep(10);
        //    if (threadList.Find(x => x.IsAlive) == null && workCount > 0)
        //    {
        //        CompareDirectory(left, right, diffFileList, onStep, totalProcessCount, ref curProcessCount, true);
        //        return;
        //    }
        //}
        lock (this)
        {
            diffFileList.AddRange(tempDiffList);
            //foreach (var strPath in diffFileList)
            //{
            //    if (string.IsNullOrEmpty(strPath))
            //        Debug.LogError("Null Path");
            //}
        }
        //文件比较完成
        curProcessCount++;
        if (onStep != null)
            onStep.Invoke(curProcessCount, totalProcessCount);
        if (!recursion) return;
        //比对目录
        string[] rightDirs = Directory.GetDirectories(right, "*", SearchOption.TopDirectoryOnly);
        foreach (var rightDir in rightDirs)
        {
            //获取所对比的源目录路径
            string leftFilePath = GetLeftPathByRightPath(left, right, rightDir);
            //不存在，新增则直接添加至列表
            if (!Directory.Exists(leftFilePath))
            {
                lock (this)
                {
                    diffFileList.Add(rightDir);
                }
                continue;
            }
            //循环处理
            CompareDirectory(leftFilePath, rightDir, diffFileList, onStep, totalProcessCount, ref curProcessCount, true);
        }
    }

    /// <summary>
    /// 在对比源目录下，获取同名文件/目录
    /// </summary>
    /// <param name="left">源目录</param>
    /// <param name="right">新文件目录</param>
    /// <param name="rightSinglePath">处于新文件目录下，一个文件或子目录</param>
    /// <returns></returns>
    private string GetLeftPathByRightPath(string left, string right, string rightSinglePath)
    {
        return left + rightSinglePath.Replace(right, "");
    }

    private void CompareSingleFile(string leftFile, string rightFile, List<string> diffFileList)
    {
        bool diff = false;
        FileInfo infoRight = new FileInfo(rightFile);
        FileInfo infoLeft = new FileInfo(leftFile);
        if (infoRight.Length != infoLeft.Length)
        {
            diff = true;
            lock (this)
            {
                diffFileList.Add(rightFile);
            }
        }
        else
        //实际文件二进制对比，比较慢
        {
            try
            {
                using (FileStream rightStream = infoRight.OpenRead())
                {
                    using (FileStream leftStream = infoLeft.OpenRead())
                    {
                        byte[] tempRArray = new byte[rightStream.Length];
                        rightStream.Read(tempRArray, 0, tempRArray.Length);
                        byte[] tempLArray = new byte[leftStream.Length];
                        leftStream.Read(tempLArray, 0, tempLArray.Length);
                        for (int i = 0; i < tempRArray.Length; i++)
                        {
                            if (tempRArray[i] != tempLArray[i])
                            {
                                diff = true;
                                lock (this)
                                {
                                    diffFileList.Add(rightFile);
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string error = string.Format("{0},{1}{2}  路径位于：Left:{3}\nRight{4}", ErrorMark, ex.Message, ex.StackTrace, leftFile, rightFile);
                lock (this)
                {
                    diffFileList.Insert(0, error);
                }
                Debug.LogError(error);
                return;
            }
        }

        if (!diff && Info.IsCompareFileDate && Info.IsSyncFileDate)
            File.SetLastWriteTime(rightFile, File.GetLastWriteTime(leftFile));
    }

    //=====================================================

    public class InfoSetting
    {
        /// <summary>
        /// 上次保存相关设置时，项目版本号
        /// </summary>
        public string LastVersion;

        /// <summary>
        /// 用于对比的原始目录
        /// </summary>
        public string LeftComprePath;

        /// <summary>
        /// 用于对比的目录
        /// </summary>
        public string RightComprePath;

        /// <summary>
        /// 保存差异文件目录
        /// </summary>
        public string DiffPatchPath;

        /// <summary>
        /// 每次打包均需确认补丁目录
        /// </summary>
        public bool MustConfirmDiffPatchPath = true;

        /// <summary>
        /// 是否压缩创建差异文件
        /// </summary>
        public bool IsCompressDiffFile = true;

        /// <summary>
        /// 日期对比
        /// </summary>
        public bool IsCompareFileDate = true;

        /// <summary>
        /// 比较完成后，若双方文件内容一致，日期不一致，是否修改比较对象日期为源文件日期
        /// </summary>
        public bool IsSyncFileDate = false;

        /// <summary>
        /// 是否排序
        /// </summary>
        public bool SortByDate = true;

        /// <summary>
        /// 进度，针对Normal模式，false可减少时间消耗
        /// </summary>
        public bool ShowProgress = true;

        /// <summary>
        /// 比较时排除的文件后缀，小写
        /// </summary>
        public string[] ExcludeSuffix = new string[] { ".meta" };

        /// <summary>
        /// 比较方式
        /// </summary>
        public ECompareMethod CompareMethod = ECompareMethod.Normal;

        /// <summary>
        /// 比较算法模式
        /// </summary>
        public ECompareMode CompareMode = ECompareMode.Normal;

        /// <summary>
        /// Super比较算法使用，最大线程数量限制
        /// 注：这里指文件夹，文件还会额外并发
        /// </summary>
        public int CompareThreadLimit = 120;

        /// <summary>
        /// BeyondCompare 程序路径
        /// </summary>
        public string BeyondCompareExePath;

        /// <summary>
        /// 检查路径后缀，是否存在排除列表中
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool CheckSuffix(string path)
        {
            path = path.ToLower();
            foreach (var item in ExcludeSuffix)
            {
                if (path.EndsWith(item))
                    return true;
            }
            return false;
        }

        public string GetExcludeSuffixString()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < ExcludeSuffix.Length; i++)
            {
                builder.Append(ExcludeSuffix[i]);
                if (i < ExcludeSuffix.Length - 1)
                    builder.Append('|');
            }
            return builder.ToString();
        }

        public void SetExludeSuffixString(string str)
        {
            string[] strList = str.Split('|');
            ExcludeSuffix = new string[strList.Length];
            for (int i = 0; i < ExcludeSuffix.Length; i++)
            {
                ExcludeSuffix[i] = strList[i];
            }
        }
    }

    public enum ECompareMethod
    {
        VeryFast,
        Normal,
        BeyondCompare,
    }

    public enum ECompareMode
    {
        Normal,
        Super_Ex,
    }

}