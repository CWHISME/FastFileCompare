using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **********************************************************
///用于文件比较，Editor表现类
///Made By Wangjiaying
///2020.7.1
///**********************************************************
/// </summary>
public class FileCompareWindow : EditorWindow
{
    /// <summary>
    /// 对比器
    /// </summary>
    private FileCompare _fileCompare;

    //处于文件比较中
    private bool _isComparing = false;
    //是否正在显示进度条
    private bool _isComparingUI = false;
    //进度信息
    private int _compareCurProcess, _compareMaxProcess;
    //创建补丁中
    private bool _isInCreatePatch = false;
    //是否处于创建补丁的提示进度
    private bool _isPatchingUI = false;
    //补丁创建成功后，返回的存储目录
    private string _patchingPath;
    //文件比较结果，差异文件列表
    private List<string> _compareResult;
    private List<string> _compareResultDis;
    //private List<string> _allCompareResultString;
    //翻页相关
    private const int _pagePerCount = 50;
    private int _pageMaxCount;
    private int _pageIndex = 0;

    private Vector2 _diffFileScrollPos;
    private Stopwatch _compareStopWatch;
    private string _compareStopWatchDisplayString;
    private Thread _thread;

    //GUI
    private GUIStyle _normalRichTextStyle;

    [MenuItem("Tools/文件对比器")]
    public static void Open()
    {
        GetWindow<FileCompareWindow>("文件对比器", true);
    }

    private void OnEnable()
    {
        _normalRichTextStyle = new GUIStyle();
        _normalRichTextStyle.wordWrap = true;
        _normalRichTextStyle.clipping = TextClipping.Overflow;
        _normalRichTextStyle.richText = true;
        _normalRichTextStyle.normal.textColor = new Color32(190, 190, 190, 255);
    }

    private void OnGUI()
    {
        if (_fileCompare == null)
            _fileCompare = new FileCompare();

        DrawPathSelector("原始目录：", ref _fileCompare.Info.LeftComprePath);
        DrawPathSelector("对比目录：", ref _fileCompare.Info.RightComprePath);
        DrawPathSelector("补丁目录：", ref _fileCompare.Info.DiffPatchPath);
        EditorGUILayout.LabelField("当前存储配置版本：" + _fileCompare.Info.LastVersion);
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = !_isComparing && !_isInCreatePatch;
        if (GUILayout.Button("对比", GUILayout.Width(70)))
        {
            _compareCurProcess = 0;
            _isComparing = true;
            _compareStopWatch = Stopwatch.StartNew();
            _compareStopWatch.Start();
            if (_thread != null) _thread.Abort();
            _thread = _fileCompare.Compare(OnCompareStep, OnCompareEnd, _fileCompare.Info.SortByDate);
        }
        //文件筛选设置
        GUILayout.Space(10);
        //排除后缀
        DrawExculdeSuffix();
        EditorGUILayout.EndHorizontal();
        //比较模式
        DrawCompareMode();
        //Bool比较设置项
        DrawCompareToggleSetting();
        //帮助提示
        StringBuilder infoTipBuilder = new StringBuilder();
        infoTipBuilder.AppendLine(LangHelp1);
        if (_fileCompare.Info.IsCompareFileDate)
            infoTipBuilder.AppendLine(LangHelp2);
        if (_fileCompare.Info.CompareMode == FileCompare.ECompareMode.Super_Ex)
            infoTipBuilder.AppendLine(LangHelp3);
        EditorGUILayout.HelpBox(infoTipBuilder.ToString(), MessageType.Info);

        //计数器
        if (_compareStopWatch != null)
            if (_compareStopWatch.IsRunning && _isComparing)
                EditorGUILayout.LabelField(CalcStopWatchString());
            else EditorGUILayout.LabelField(_compareStopWatchDisplayString);
        //若比较结果存在，则显示出来
        _compareResultDis = _compareResult;
        if (_compareResultDis != null)
        {
            DrawPatchBtn();
            EditorGUILayout.LabelField(string.Format(LangCompareSummaryTips, _compareMaxProcess, _compareResultDis.Count), _normalRichTextStyle);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            _diffFileScrollPos = EditorGUILayout.BeginScrollView(_diffFileScrollPos);
            int thisPageIndex = _pageIndex * _pagePerCount;
            for (int i = thisPageIndex; i < _compareResultDis.Count && i < thisPageIndex + _pagePerCount; i++)
            {
                string path = _compareResultDis[i];
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("O", GUILayout.Width(20)))
                    EditorUtility.OpenWithDefaultApp(Path.GetDirectoryName(_compareResultDis[i]));
                bool useLeftFile = _fileCompare.Info.CreatePatchUseLeftFile.Contains(path);
                EditorGUI.BeginChangeCheck();
                useLeftFile = EditorGUILayout.Toggle(useLeftFile, GUILayout.Width(10));
                if (EditorGUI.EndChangeCheck())
                {
                    if (useLeftFile && !_fileCompare.Info.CreatePatchUseLeftFile.Contains(path))
                        _fileCompare.Info.CreatePatchUseLeftFile.Add(path);
                    else _fileCompare.Info.CreatePatchUseLeftFile.Remove(path);
                    Save();
                }
                EditorGUILayout.LabelField(CalcDisplayPathStr(i + 1, path), _normalRichTextStyle);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            DrawCreatePatchUseLeftFileSetting();
            GUILayout.Space(20);
        }
        DrawPageSetting();
        GUI.enabled = true;

        //检查是否显示/清理比较进度条
        CheckDisplayProgress();
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnDestroy()
    {
        if (_thread != null)
            _thread.Abort();
    }

    /// <summary>
    /// 比较中
    /// </summary>
    private void OnCompareStep(int cur, int max)
    {
        _compareCurProcess = cur;
        _compareMaxProcess = max;
    }

    /// <summary>
    /// 当比较程序完成
    /// </summary>
    /// <param name="list"></param>
    private void OnCompareEnd(bool isSuccess, List<string> list)
    {
        _compareResult = list;
        _isComparing = false;
        _pageMaxCount = Mathf.CeilToInt(_compareResult.Count / (float)_pagePerCount);
        _pageIndex = 0;
        _compareStopWatch.Stop();
        _compareStopWatchDisplayString = CalcStopWatchString();
    }

    /// <summary>
    /// 补丁复制程序完成
    /// </summary>
    /// <param name="list"></param>
    private void OnCreatePatchEnd(string path)
    {
        _isPatchingUI = true;
        _isInCreatePatch = false;
        _patchingPath = path;
        _compareStopWatch.Stop();
    }

    private string CalcStopWatchString()
    {
        return (string.Format(LangTimeCounterTips, _compareStopWatch.Elapsed.Minutes.ToString().PadLeft(2, '0'), _compareStopWatch.Elapsed.Seconds.ToString().PadLeft(2, '0'), _compareStopWatch.Elapsed.Milliseconds.ToString().PadLeft(3, '0')));
    }

    private string CalcDisplayPathStr(int index, string path)
    {
        bool exist = Directory.Exists(path) || File.Exists(path);
        DateTime date = exist ? File.GetLastWriteTime(path) : DateTime.MaxValue;
        if (!exist)
        {
            return (string.Format(LangInternalError, path));
        }
        bool isNow = (date.Year == DateTime.Now.Year && date.DayOfYear == DateTime.Now.DayOfYear);
        bool isDir = Directory.Exists(path);
        string isNullStr = "";
        if (_fileCompare.Info.CreatePatchUseLeftFile.Contains(path))
        {
            path = _fileCompare.GetLeftPathByRightPath(path);
            if (!Directory.Exists(path) && !File.Exists(path))
                isNullStr = LangNullFilePath;
            path = string.Format("<b>{0}</b>", path);
        }
        return (string.Format("{0}. {3}[{1}] {2}{4} {5}", (index).ToString().PadLeft(3, '0'), date.ToString("yyyy-MM-dd hh:mm:ss"), path, isNow ? LangTodayMofiyTips : string.Empty, isDir ? LangDir : string.Empty, isNullStr));
    }
    //================UI封装============================
    /// <summary>
    /// 比较目录路径选择
    /// </summary>
    private void DrawPathSelector(string title, ref string path)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(title + (string.IsNullOrEmpty(path) ? LangNotPath : path), _normalRichTextStyle);
        if (GUILayout.Button("选择", GUILayout.Width(70)))
        {
            OpenDirChoose(ref path);
        }
        if (GUILayout.Button("打开", GUILayout.Width(70)))
        {
            EditorUtility.OpenWithDefaultApp(path);
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 比较模式
    /// </summary>
    private void DrawCompareMode()
    {
        EditorGUI.BeginChangeCheck();
        _fileCompare.Info.CompareMode = (FileCompare.ECompareMode)EditorGUILayout.EnumPopup("比较模式：", _fileCompare.Info.CompareMode, GUILayout.Width(250));
        if (_fileCompare.Info.CompareMode == FileCompare.ECompareMode.Super_Ex)
            _fileCompare.Info.CompareThreadLimit = EditorGUILayout.IntField("同时执行的线程数量限制：", _fileCompare.Info.CompareThreadLimit, GUILayout.Width(250));
        if (EditorGUI.EndChangeCheck())
            Save();
    }

    /// <summary>
    /// 属于Toggle相关设置
    /// </summary>
    private void DrawCompareToggleSetting()
    {
        GUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _fileCompare.Info.IsCompareFileDate = EditorGUILayout.ToggleLeft("日期对比", _fileCompare.Info.IsCompareFileDate, GUILayout.Width(70));
        if (_fileCompare.Info.IsCompareFileDate)
            _fileCompare.Info.IsSyncFileDate = EditorGUILayout.ToggleLeft("同步日期", _fileCompare.Info.IsSyncFileDate, GUILayout.Width(70));
        if (_fileCompare.Info.CompareMode == FileCompare.ECompareMode.Normal)
            _fileCompare.Info.ShowProgress = EditorGUILayout.ToggleLeft("进度统计", _fileCompare.Info.ShowProgress, GUILayout.Width(70));
        _fileCompare.Info.SortByDate = EditorGUILayout.ToggleLeft("日期排序", _fileCompare.Info.SortByDate, GUILayout.Width(70));
        if (EditorGUI.EndChangeCheck())
            Save();
        EditorGUILayout.EndHorizontal();
    }

    private string _suffixExcludeString;
    private bool _editExcludeSuffix;
    /// <summary>
    /// 排除后缀选项
    /// </summary>
    private void DrawExculdeSuffix()
    {
        if (string.IsNullOrEmpty(_suffixExcludeString))
            _suffixExcludeString = _fileCompare.Info.GetExcludeSuffixString();

        if (_editExcludeSuffix)
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("排除后缀文件：");
            _suffixExcludeString = EditorGUILayout.TextArea(_suffixExcludeString, GUILayout.Height(50));
            if (GUILayout.Button("确定", GUILayout.Width(70)))
            {
                _fileCompare.Info.SetExludeSuffixString(_suffixExcludeString);
                _editExcludeSuffix = false;
                Save();
            }
            if (GUILayout.Button("取消", GUILayout.Width(70)))
            {
                _suffixExcludeString = _fileCompare.Info.GetExcludeSuffixString();
                _editExcludeSuffix = false;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(LangExcludeSuffixHelp, MessageType.Info);
            EditorGUILayout.EndVertical();
        }
        else
        {
            EditorGUILayout.LabelField("排除后缀文件：" + _suffixExcludeString);
            if (GUILayout.Button("编辑", GUILayout.Width(70)))
            {
                _editExcludeSuffix = true;
            }  
        }
    }

    /// <summary>
    /// 创建补丁使用源 设置
    /// </summary>
    private void DrawCreatePatchUseLeftFileSetting()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全选", GUILayout.Width(70)))
        {
            if (EditorUtility.DisplayDialog(LangTipsTitle, LangUseLeftFileForCreatePatchTips, LangOk, LangCancel))
            {
                _fileCompare.Info.CreatePatchUseLeftFile.Clear();
                _fileCompare.Info.CreatePatchUseLeftFile.AddRange(_compareResult);
                Save();
            }
        }
        if (GUILayout.Button("清空", GUILayout.Width(70)))
        {
            if (EditorUtility.DisplayDialog(LangTipsTitle, LangConfirmClearUseLeftFileForCreatePatchSelectTips, LangOk, LangCancel))
            {
                _fileCompare.Info.CreatePatchUseLeftFile.Clear();
                Save();
            }
        }
        EditorGUILayout.LabelField(LangUseLeftFileForCreatePatchHelp, _normalRichTextStyle);
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 翻页设置
    /// </summary>
    private void DrawPageSetting()
    {
        //if (_pageMaxCount < 2) return;
        Vector2 startPos = new Vector2(position.width - 115, position.height - 30);
        Vector2 buttonSize = new Vector2(20, 20);
        if (GUI.Button(CalcRect(ref startPos, 20, 20), "<"))
            HoldPageIndex(_pageIndex - 1);
        _normalRichTextStyle.alignment = TextAnchor.UpperCenter;
        EditorGUI.LabelField(CalcRect(ref startPos, 65, 30), string.Format("第{0}/{1}页", _pageIndex + 1, _pageMaxCount), _normalRichTextStyle);
        _normalRichTextStyle.alignment = TextAnchor.UpperLeft;
        if (GUI.Button(CalcRect(ref startPos, 20, 20), ">"))
            HoldPageIndex(_pageIndex + 1);
    }

    private Rect CalcRect(ref Vector2 startPos, int width, int height)
    {
        Rect rect = new Rect(startPos, new Vector2(width, height));
        startPos.x = startPos.x + width;
        return rect;
    }

    private void HoldPageIndex(int index)
    {
        if (index < 0)
            index = 0;
        else if (index >= _pageMaxCount)
            index = _pageMaxCount - 1;
        _pageIndex = index;
    }

    /// <summary>
    /// 比较进度条
    /// </summary>
    private void CheckDisplayProgress()
    {
        if (_fileCompare.Info.CompareMode == FileCompare.ECompareMode.Normal && !_fileCompare.Info.ShowProgress) return;
        if (_isComparing || _isInCreatePatch)
        {
            if (!_isComparingUI)
                _isComparingUI = true;
            EditorUtility.DisplayProgressBar(string.Format("操作中......({0}s)", (int)_compareStopWatch.Elapsed.TotalSeconds), string.Format("当前进度：{0}/{1}", _compareCurProcess, _compareMaxProcess), _compareCurProcess / (float)_compareMaxProcess);
        }
        else if (_isComparingUI)
        {
            EditorUtility.ClearProgressBar();
            if (_isPatchingUI)
            {
                _isPatchingUI = false;
                EditorUtility.OpenWithDefaultApp(_patchingPath);
            }
            _isComparingUI = false;
        }
    }

    /// <summary>
    /// 创建补丁
    /// </summary>
    private void DrawPatchBtn()
    {
        EditorGUILayout.BeginHorizontal(GUI.skin.box);
        if (GUILayout.Button("创建补丁", GUILayout.Width(80)))
        {
            _isInCreatePatch = true;
            _compareStopWatch = Stopwatch.StartNew();
            _compareStopWatch.Start();
            if (_fileCompare.Info.MustConfirmDiffPatchPath || string.IsNullOrEmpty(_fileCompare.Info.DiffPatchPath))
            {
                if (OpenDirChoose(ref _fileCompare.Info.DiffPatchPath))
                    _fileCompare.CreatePatch(_compareResult, "Patcher", _fileCompare.Info.IsCompressDiffFile, OnCompareStep, OnCreatePatchEnd);
            }
            else _fileCompare.CreatePatch(_compareResult, "Patcher", _fileCompare.Info.IsCompressDiffFile, OnCompareStep, OnCreatePatchEnd);
        }
        EditorGUI.BeginChangeCheck();
        _fileCompare.Info.MustConfirmDiffPatchPath = EditorGUILayout.ToggleLeft("始终询问保存目录", _fileCompare.Info.MustConfirmDiffPatchPath, GUILayout.Width(110));
        _fileCompare.Info.IsCompressDiffFile = EditorGUILayout.ToggleLeft("压缩", _fileCompare.Info.IsCompressDiffFile);
        if (EditorGUI.EndChangeCheck())
            Save();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 开启目录选择
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private bool OpenDirChoose(ref string path)
    {
        string tempPath = EditorUtility.OpenFolderPanel("选择", path, "");
        if (!string.IsNullOrEmpty(tempPath))
        {
            path = tempPath;
            Save();
            return true;
        }
        return false;
    }
    //=================================================

    private void Save()
    {
        _fileCompare.Info.LastVersion = Application.version;
        _fileCompare.SaveSetting();
    }

    //=================实体文字============================
    private const string LangNotPath = "<color=red>注意：未选择目录！</color>";
    private const string LangNullFilePath = "<color=red>原始目录资源不存在！</color>";
    private const string LangInternalError = "<color=red>内部错误：{0}</color>";
    private const string LangTodayMofiyTips = "[<color=#5ADD53FF>今日修改！</color>]";
    private const string LangDir = "[<color=yellow>文件夹</color>]";
    private const string LangTimeCounterTips = "**********************************消耗：{0}:{1}:{2}************************************";
    private const string LangCompareSummaryTips = "  共对比<color=yellow>{0}</color>个文件目录，差异文件数量：<color=#00F4FFFF>{1}</color>个";
    private const string LangHelp1 = "比较时，将以 “对比目录” 数据为基础与原始目录进行比较，并得出差异化文件或目录。";
    private const string LangHelp2 = "勾选同步日期时，在两个文件二进制一致情况， 会同步对比目录与原始目录一致。第一次对比时长会增加，不过同时会减少下次对比时消耗的时间。";
    private const string LangHelp3 = "注：线程数量限制针对于文件夹，文件对比时还会额外并发。";
    private const string LangExcludeSuffixHelp = "若有多个需要排除的后缀，请以 “|” 分割。(如：“.meta|.txt|.mp4”)";
    private const string LangTipsTitle = "提示";
    private const string LangOk = "确定";
    private const string LangCancel = "取消";
    private const string LangUseLeftFileForCreatePatchTips = "该操作将会使创建补丁时，当前所有差异文件均使用源目录中同路径文件，是否确认选择？";
    private const string LangConfirmClearUseLeftFileForCreatePatchSelectTips = "是否确认清空使用源的路径选择？";
    private const string LangUseLeftFileForCreatePatchHelp = "<color=#4DFF7AFF>勾选选中框，代表创建补丁时，使用源目录同路径下文件(若存在)</color>";


    //==================================================
}