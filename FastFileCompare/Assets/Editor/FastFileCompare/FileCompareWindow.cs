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
    private bool _isComparingUI = false;
    private int _compareCurProcess, _compareMaxProcess;
    private bool _isInCreatePatch = false;
    private bool _isPatchingUI = false;
    private string _patchingPath;
    //文件比较结果，差异文件列表
    private List<string> _compareResult;
    private List<string> _allCompareResultString;

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
            _thread = _fileCompare.Compare(OnCompareStep, OnCompareEnd, true);
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
        infoTipBuilder.AppendLine("比较时，将以 “对比目录” 数据为基础与原始目录进行比较，并得出差异化文件或目录。");
        if (_fileCompare.Info.IsCompareFileDate)
            infoTipBuilder.AppendLine("勾选同步日期时，在两个文件二进制一致情况， 会同步对比目录与原始目录一致。第一次对比时长会增加，不过同时会减少下次对比时消耗的时间。");
        EditorGUILayout.HelpBox(infoTipBuilder.ToString(), MessageType.Info);

        //计数器
        if (_compareStopWatch != null)
            if (_compareStopWatch.IsRunning && _isComparing)
                EditorGUILayout.LabelField(CalcStopWatchString());
            else EditorGUILayout.LabelField(_compareStopWatchDisplayString);
        //若比较结果存在，则显示出来
        if (_compareResult != null && _allCompareResultString != null)
        {
            DrawPatchBtn();
            EditorGUILayout.LabelField(string.Format("  共对比<color=yellow>{0}</color>个文件目录，差异文件数量：<color=#00F4FFFF>{1}</color>个", _compareMaxProcess, _compareResult.Count), _normalRichTextStyle);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            _diffFileScrollPos = EditorGUILayout.BeginScrollView(_diffFileScrollPos);
            foreach (var item in _allCompareResultString)
            {
                EditorGUILayout.LabelField(item, _normalRichTextStyle);
            }
            //DateTime nowTime = DateTime.Now;
            //for (int i = 0; i < _compareResult.Count; i++)
            //{
            //    DateTime date = /*Directory.Exists(_compareResult[i]) ? Directory.GetLastWriteTime(_compareResult[i]) :*/ File.GetLastWriteTime(_compareResult[i]);
            //    if (date.Year == nowTime.Year && date.DayOfYear == nowTime.DayOfYear)
            //        EditorGUILayout.LabelField(string.Format("{0}. [<color=#5ADD53FF>今日修改！</color>][{1}] {2}", (i + 1), date.ToString("yyyy-MM-dd hh:mm:ss"), _compareResult[i]), _normalRichTextStyle);
            //    else EditorGUILayout.LabelField(string.Format("{0}. [{1}] {2}", (i + 1), date.ToString("yyyy-MM-dd hh:mm:ss"), _compareResult[i]), _normalRichTextStyle);
            //    if (i > 201)
            //    {
            //        EditorGUILayout.LabelField("************************************超出预览数量限制！************************************");
            //        EditorGUILayout.LabelField("************************************超出预览数量限制！************************************");
            //        break;
            //    }
            //}
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
        GUI.enabled = true;

        //检查是否显示/清理比较进度条
        CheckDisplayProgress();
    }

    private void Update()
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
        _allCompareResultString = new List<string>();
        if (!isSuccess)
        {
            _allCompareResultString.Add(string.Format("<color=red>出现错误！对比失败：{0}</color>", list[0]));
            list.Clear();
        }
        _compareResult = list;
        _isComparing = false;
        StringBuilder builder = new StringBuilder();
        DateTime nowTime = DateTime.Now;
        string nowStringTip = "[<color=#5ADD53FF>今日修改！</color>]";
        string dirTip = "[<color=yellow>文件夹</color>]";
        int limitNum = 0;
        for (int i = 0; i < _compareResult.Count; i++)
        {
            DateTime date = /*Directory.Exists(_compareResult[i]) ? Directory.GetLastWriteTime(_compareResult[i]) :*/ File.GetLastWriteTime(_compareResult[i]);
            string str;
            bool isNow = (date.Year == nowTime.Year && date.DayOfYear == nowTime.DayOfYear);
            bool isDir = Directory.Exists(_compareResult[i]);
            str = (string.Format("{0}. {3}[{1}] {2}{4}", (i + 1).ToString().PadLeft(3, '0'), date.ToString("yyyy-MM-dd hh:mm:ss"), _compareResult[i], isNow ? nowStringTip : string.Empty, isDir ? dirTip : string.Empty));
            builder.AppendLine(str);
            limitNum++;
            if (limitNum > 99)
            {
                limitNum = 0;
                builder.Remove(builder.Length - 2, 2);
                _allCompareResultString.Add(builder.ToString());
                builder.Remove(0, builder.Length);
            }
        }
        _allCompareResultString.Add(builder.ToString());
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
        return (string.Format("**********************************消耗：{0}:{1}:{2}************************************", _compareStopWatch.Elapsed.Minutes.ToString().PadLeft(2, '0'), _compareStopWatch.Elapsed.Seconds.ToString().PadLeft(2, '0'), _compareStopWatch.Elapsed.Milliseconds.ToString().PadLeft(3, '0')));
    }
    //================UI封装============================
    /// <summary>
    /// 比较目录路径选择
    /// </summary>
    private void DrawPathSelector(string title, ref string path)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(title + path);
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
            _suffixExcludeString = EditorGUILayout.TextField(_suffixExcludeString);
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
            EditorGUILayout.HelpBox("若有多个需要排除的后缀，请以 “|” 分割。(如：“.meta|.txt|.mp4”)", MessageType.Info);
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
    /// 比较进度条
    /// </summary>
    private void CheckDisplayProgress()
    {
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

}