# FastFileCompare
多线程实现的快速文件对比工具，提供UI界面进行对比设置及可视化操作，并提供核心类供代码直接调用。

![](https://github.com/CWHISME/Unity3D-CheatSystem/raw/master/RawImg/Snipaste_2020-07-04_11-22-41.png)

## 使用

使用编辑器在UI上设置好路径之后，后续若代码链接工作流
默认代码可调用：
``` cs
	FileCompare compare=new FileCompare();
	compare.Compare(null,onEnd);
```
得出结果时，将自动调用onEnd回调，若需返回结果按日期排序，第三个参数则传入true。

比较时，将以 “对比目录” 数据为基础与原始目录进行比较，并得出差异化文件或目录。
勾选同步日期时，在两个文件二进制一致情况， 会同步对比目录与原始目录一致。第一次对比时长会增加，不过同时会减少下次对比时消耗的时间。
使用Super_Ex对比模式时，会并对文件夹发更多线程执行对比。注：线程数量限制针对于文件夹，文件对比时还会额外并发。