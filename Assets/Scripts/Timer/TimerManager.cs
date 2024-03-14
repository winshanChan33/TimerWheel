/*
    定时器管理器，用于驱动时间轮的推进，和提供外部接口
*/

using System;
using System.Collections.Generic;
using UnityEngine;

public class TimerManager
{
    private const int k_TV0Bits = 8;                     // tv0：0-255,256个槽位
    private const int k_TVNBits = 6;                     // tv1-tv4层：0-63,64个槽位
    private const int k_TV0Size = 1 << k_TV0Bits;        // tv0，槽位个数
    private const int k_TVNSize = 1 << k_TVNBits;        // t1-t4层，槽位个数

    // 位操作掩码，用于计算指定expireTime属于哪个槽位下标
    private const int k_TV0Mask = k_TV0Size - 1;
    private const int k_TVNMask = k_TVNSize - 1;
    
    private const int k_TVLevel = 5;                    // 层级轮层级个数

    private const int k_MsPerTick = 10;                 // 10ms执行一次tick
    private const float k_SecondPerTick = k_MsPerTick / 1000f;

    public static TimerManager Instance { get; } = new TimerManager();

    // 用一整个数组存储所有时间槽位，计算时再在把内部划分成5层（连续内存）
    private TimerSolt[] m_TimeSoltsArray = new TimerSolt[k_TV0Size + (k_TVLevel - 1) * k_TVNSize];
    // 临时链表，避免某个槽位向下展开时执行其他操作导致数据异常
    private LinkedList<TimerTask> m_TmpLinkList = new();
    private Dictionary<int, TimerTask> m_AllTimerMap = new();
    // 延迟回收列表，update时不能进行回收处理
    private List<TimerTask> m_DelayRecycleList = new();
    // 定时器任务对象池
    private ObjectPool<TimerTask> m_TaskInstancePool = new();

    private int m_IDSeed = 0;                           // 用于生成自增随机ID
    
    // 定时器初始化时刻tick
    private long m_StartTick;
    // 当前走到的tick，此前的定时器全部执行完毕
    private long m_CurTick;
    // 当前需要执行到的tick，根据系统时间计算一次Update需要走到的tick
    private long m_CurTargetTick;

    // 计算当前应该走到的tick位置
    private long CalcCurTick()
    {
        return (long)(Time.time / k_SecondPerTick) - m_StartTick;
    }

    // 毫秒转为为tick
    private long MSecond2Tick(long mSecond)
    {
        return mSecond / k_MsPerTick;
    }

    /*
        对高阶轮某个槽位，对低阶轮进行的展开操作

        param tvOffset：相对于展开目标轮的偏移。如：t1轮则偏移为t0轮的长度，256
        param soltIdx：当前需要展开的槽位下标
    */
    private int TimerCascade(int tvOffset, int soltIdx)
    {
        var index = tvOffset + soltIdx;
        var solt = m_TimeSoltsArray[index];
        var tasks = solt.SoltTasks;
        if (tasks.Count > 0)
        {
            m_TimeSoltsArray[index].SoltTasks = m_TmpLinkList;
            m_TmpLinkList = tasks;
            while(m_TmpLinkList.Count > 0)
            {
                var task = m_TmpLinkList.First;
                m_TmpLinkList.Remove(task);
                InternalAddTimer(task.Value);
            }
        }

        return soltIdx;
    }

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

    // 新增一个定时器任务到时间轮中，外部新增定时器时调用该方法
    private void InternalAddTimer(TimerTask timer, bool fromCascade = false)
    {
        var expireTime = timer.Expire;            // 定时器超时时间差
        var span = expireTime - m_CurTargetTick;      // 此处需要用目标tick算，避免间隔帧内执行异常
        var soltIndex = -1;
        if (span < 0)
        {
            // 过期定时器
            if (fromCascade)
            {
                // 从高阶轮展开的过期定时器需要马上执行
                soltIndex = CalcTV0Index(m_CurTick);
            }
            else
            {
                // 可以下一帧执行
                soltIndex = CalcTV0Index(m_CurTargetTick);
            }
        }
        else if (span < k_TV0Size)
        {
            soltIndex = CalcTV0Index(expireTime);
        }
        else if (span < 1 << (k_TV0Bits + k_TVNBits))
        {
            soltIndex = k_TV0Size + CalcSoltIndex(expireTime, 0);
        }
        else if (span < 1 << (k_TV0Bits + 2 * k_TVNBits))
        {
            soltIndex = k_TV0Size + k_TVNSize + CalcSoltIndex(expireTime, 1);
        }
        else if (span < 1 << (k_TV0Bits + 3 * k_TVNBits))
        {
            soltIndex = k_TV0Size + 2 * k_TVNSize + CalcSoltIndex(expireTime, 2);
        }
        else
        {
            // 定时器过期时间超过该时间轮的最大限制
            if (span > 0xffffffffL)
            {
                expireTime = m_CurTargetTick + 0xffffffffL;
            }

            soltIndex = k_TV0Size + 3 * k_TVNSize + CalcSoltIndex(expireTime, 3);
        }

        timer.SoltIndex = soltIndex;
        var targetList = m_TimeSoltsArray[soltIndex].SoltTasks;
        targetList.AddLast(timer);
    }

    #region 外部调度接口

    // 初始化定时器
    public void Init()
    {
        for (int i = 0; i < m_TimeSoltsArray.Length; i++)
        {
            m_TimeSoltsArray[i] = new TimerSolt();
        }
        m_StartTick = CalcCurTick();
        m_CurTargetTick = CalcCurTick();
        m_CurTick = CalcCurTick();
    }

    // 更新时间轮进度，由外部推动
    public void Update()
    {
        m_CurTargetTick = CalcCurTick();
        while (m_CurTick < m_CurTargetTick)
        {
            var index = CalcTV0Index(m_CurTick);
            if (index == 0
                && TimerCascade(k_TV0Size, CalcSoltIndex(m_CurTick, 0)) == 0
                && TimerCascade(k_TV0Size + k_TVNSize, CalcSoltIndex(m_CurTick, 1)) == 0
                && TimerCascade(k_TV0Size + 2 * k_TVNSize, CalcSoltIndex(m_CurTick, 2)) == 0)
            {
                TimerCascade(k_TV0Size + 3 * k_TVNSize, CalcSoltIndex(m_CurTick, 3));
            }
            
            var invokeTasks = m_TimeSoltsArray[index].SoltTasks;
            if (invokeTasks.Count > 0)
            {
                m_TimeSoltsArray[index].SoltTasks = m_TmpLinkList;
                m_TmpLinkList = invokeTasks;
                while(m_TmpLinkList.Count > 0)
                {
                    var task = m_TmpLinkList.First;
                    m_TmpLinkList.Remove(task);
                    // TODO 执行时isValid判断
                    // 在此处进行回池处理，去掉m_DelayRecycleList
                    bool nextStatus = task.Value.Invoke();        // 执行最低颗粒度轮的过期槽位中的所有定时器任务
                    if (nextStatus)
                    {
                        task.Value.UpdateNextExpire(CalcCurTick());
                        InternalAddTimer(task.Value);
                    }
                    else
                    {
                        // 清除过期task对象
                        task.Value.Reset();
                        m_TaskInstancePool.Return(task.Value);
                    }
                }
            }

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
    #endregion 

    #region 外部业务接口

    /*
        新增一个定时器

        param interval：时间间隔，单位：秒
        param times：执行次数，-1视为执行无限次
        param callback：任务回调
        param userData：回调用户自定义参数
    */
    public int AddTimer(float interval, int times = 1, Action<object[]> callback = null, object[] userData = null)
    {
        return AddMSTimer((long)(interval * 1000), times, callback, userData);
    }

    /*
        新增一个定时器

        param msInterval：时间间隔，单位：毫秒
        param times：执行次数，-1视为执行无限次
        param callback：任务回调
        param userData：回调用户自定义参数
    */
    public int AddMSTimer(long interval, int times = 1, Action<object[]> callback = null, object[] userData = null)
    {
        // TODO 打error日志return，尽量不要抛出异常。模块名 方法名
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

    /*
        删除某个定时器
    */
    public void RemoveTimer(int taskId)
    {
        // TODO 打印日志 return
        // 改为tryGetValue
        if (!m_AllTimerMap.ContainsKey(taskId))
            throw new ArgumentNullException("无效定时器对象");

        var timerTask = m_AllTimerMap[taskId];
        timerTask.IsValid = false;
        if (timerTask.SoltIndex >= 0)
        {
            // m_TimeSoltsArray[timerTask.SoltIndex].SoltTasks.Remove(timerTask);
            timerTask.IsValid = false;
        }
        m_AllTimerMap.Remove(taskId);
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

    /*
        修改定时器的属性

        param times：修改执行次数
        param callback：修改任务回调
        param userData：修改回调用户自定义参数
    */
    public void ModifyTimer(int id, int times = 0, Action<object[]> callback = null, object[] userData = null)
    {
        if (!m_AllTimerMap.ContainsKey(id) || !m_AllTimerMap[id].IsValid)
            throw new ArgumentNullException("无效定时器对象");

        var task = m_AllTimerMap[id];
        task.ModifyTimer(times, callback, userData);
    }

    #endregion
}