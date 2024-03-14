# 时间轮定时器
## 关键原理
### 位运算
源码中用一个unit32（无符号32位整形，范围足够大，32位处理效率更高）位进行划分
```
| 6bit | 6bit | 6bit | 6bit |  8bit  |
 111111 111111 111111 111111 11111111
```
源码中定义了tv0-tv4这样的五个数组存储槽位，除了第一轮占8位，其他轮次占6位。假设精确到毫秒单位，一个tick走10ms，可以支持256 * 64 * 64 * 64 * 64 * 64 * 10毫秒的最大时间（抛开时分秒日月年的限制思考）。  
通过运维算可以高效算出一个tick当前所处于的槽位下标，从而索引到对应的定时器链表。
```
/*
    计算过期时间当前处于的下标

    param expireTime：当前需要计算下标的过期时间
    param n：该轮所处于的层级
*/
private int CalcSoltIndex(long expireTime, int n)
{
    return (int)(expireTime >> (k_TV0Bits + (n * k_TVNBits)) & k_TVNMask);
}

// 计算某个过期时间在第一个轮中当前处于的下标
private int CalcTV0Index(long expireTime)
{
    return (int)(expireTime & k_TV0Mask);
}
```

### 更新逻辑
时间轮的更新逻辑关键在于每一轮走到`index == 0`时刻，上层某个槽位向下层展开的逻辑。
```
if (index == 0
    && TimerCascade(k_TV0Size, CalcSoltIndex(m_CurTick, 0)) == 0
    && TimerCascade(k_TV0Size + k_TVNSize, CalcSoltIndex(m_CurTick, 1)) == 0
    && TimerCascade(k_TV0Size + 2 * k_TVNSize, CalcSoltIndex(m_CurTick, 2)) == 0)
{
    TimerCascade(k_TV0Size + 3 * k_TVNSize, CalcSoltIndex(m_CurTick, 3));
}
```

## review遗留问题
1. 精度问题，精确到毫秒
2. 定时器最大支持时间，修改后：256 * 64 * 64 * 64 * 64 * msPerTick 毫秒
3. 准确性问题
4. 删除定时器处理
5. 定时器准确度问题
6. 简单对象池加入
7. 压测：运行期间频繁插入定时器

### 存储结构
参考源码的5个数组，修改为一个长度为256+4*64的数组，对整个数字进行内部逻辑划分，连续内存加快访问速度。

### 精度问题
修改后计算逻辑精确到毫秒，10ms走过一个tick，另外接口进行了秒/毫秒的支持。

### 准确性问题
> 此前的推进方式是从外部`Update`在delta时间内传入一个时间间隔来指定一次更新操作走过多少个tick，会存在着卡顿后精确度的问题。

修改后，直接用`Time.time`来计算时间轮内部的当前`curTargetTick`，另外持有一个此前的已经走过的`curTick`，每次更新逻辑执行的时候，应该推进
`curTargetTick - curTick`次tick。
```
private long CalcCurTick()
{
    return (long)(Time.time / k_SecondPerTick) - m_StartTick;
}
```
```
public void Update()
    {
        m_CurTargetTick = CalcCurTick();
        while (m_CurTick < m_CurTargetTick)
        {
            // 具体Update展开逻辑...

            m_CurTick++;
        }
    }
```
![执行结果](./pic/excute_1.png)

### 删除定时器处理
定时器管理内部持有一个所有定时器的映射表，主要通过外部持有的ID进行删除处理。
> 如果Update过程中进行了删除操作，怎么避免异常？

加入延迟回收队列，在Update过程中（判断`curTick < curTrargetTick`）不进行定时器实例回收，在完成一次Update只有再回收
```
public void RemoveTimer(int taskId)
{
    // 删除逻辑...

    if (m_CurTick < m_CurTargetTick)
    {
        // 执行update过程不做回池处理，避免数据错乱
        m_DelayRecycleList.Add(timerTask);
    }
    else
    {
        m_TaskInstancePool.Return(timerTask);
    }
}
```
```
public void Update()
{
    m_CurTargetTick = CalcCurTick();
    while (m_CurTick < m_CurTargetTick)
    {
        // update timer.....

        m_CurTick++;
    }

    // 更新完毕，对延迟回收对象进行处理
    if (m_DelayRecycleList.Count > 0)
    {
        foreach (var task in m_DelayRecycleList)
        {
            m_TaskInstancePool.Return(task);
        }
        m_DelayRecycleList.Clear();
    }
}
```

### 接口设计调整
1. 补充参数注释
2. 增加参数合法验证
3. 简化接口参数  
以下为新增一个毫秒定时器的接口
```
/*
    新增一个定时器

    param msInterval：时间间隔，单位：毫秒
    param times：执行次数，-1视为执行无限次
    param callback：任务回调
    param userData：回调用户自定义参数
*/
public int AddMSTimer(long interval, int times = 1, Action<object[]> callback = null, object[] userData = null)
{
    if (interval <= 0)
        throw new ArgumentException("定时器过期时间跨度不能为0或负数");

    if (times != -1 && times <= 0)
        throw new ArgumentException("定时器执行次数参数异常，可选-1或者大于0的整形");

    var timerTask = m_TaskInstancePool.Get();
    timerTask.Init(++m_IDSeed, CalcCurTick(), MSecond2Tick(interval), times, callback, userData);
    InternalAddTimer(timerTask);

    m_AllTimerMap[timerTask.ID] = timerTask;

    return timerTask.ID;
}
```

### Profiler结果
下图为此前已经开启过10w个循环定时器的情况下，运行过程每200ms开启一个定时器的Profiler结果
![测试结果](./pic/profiler.png)


## 参考来源
[源码]https://elixir.bootlin.com/linux/v2.6.39.4/source/kernel/timer.c#L598  
[参数解析]https://cloud.tencent.com/developer/article/1603333?from=15425  
[关键逻辑]https://zhuanlan.zhihu.com/p/84502375  

### TODO
1. LinkList底层实现
2. linux双向链表底层实现设计，怎么解决删除链表中间快速删除，索引计算（耗时）