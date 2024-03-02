/*
    定时器管理器，用于驱动时间轮的推进，和提供外部接口
*/

using System;
using System.Collections.Generic;

public class TimerManager
{
    public const int k_TWRBits = 8;
    public const int k_TWNBits = 6;
    public const int k_TWRSize = 1 << k_TWRBits;        // 第一层时间轮的槽位个数
    public const int k_TWNSize = 1 << k_TWNBits;      // 第二层时间轮的槽位个数
    public const int k_TWRMask = k_TWRSize - 1;            // 位操作掩码，用于计算当前定时器应该加入到哪个槽中
    public const int k_TWNMask = k_TWNSize - 1;

    public static TimerManager Instance { get; } = new TimerManager();

    private const int k_TWLevel = 5;    // 层级轮层级个数

    private TimerWheel[] m_TWArray;      // 存储所有的时间轮
    
    public long Jiffies { get { return m_Jiffies; } }
    private long m_Jiffies = 0;         // 当前走到的时间点，此前时间点的定时器全部执行完毕
    
    public bool IsRunning { get { return m_IsRunning; } }
    private bool m_IsRunning = false;

    private ObjectPool<TimerTask> m_TaskInstancePool = new();       // 定时器任务对象池

    public TimerManager()
    {
    }

    private void InitTimerWheels()
    {
        m_TWArray = new TimerWheel[k_TWLevel];
        m_TWArray[0] = new TimerWheel(k_TWRSize);
        for (int i = 0; i < k_TWLevel - 1; i++)
        {
            m_TWArray[i + 1] = new TimerWheel(k_TWNSize);
        }
    }

    // param updateTick：一次更新操作下，时间轮推进的步长。如：外部1秒执行调用一次更新，一次执行updateTick个滴答位
    private void TimerUpdate(int updateTick)
    {
        // TODO 目前jiffies是内部持有的，从0开始计算，如何校验时间准确性？DateTime。。
        var curTime = m_Jiffies;
        while (m_Jiffies < curTime + updateTick)
        {
            var w1 = m_TWArray[0];
            var w2 = m_TWArray[1];
            var w3 = m_TWArray[2];
            var w4 = m_TWArray[3];
            var w5 = m_TWArray[4];
            var index = m_Jiffies & k_TWRMask;
            if (index == 0)
            {
                // 当前指针达到0，上层也需要增进一步。此时需要对上层做展开处理
                var idx = CalcExpireWheelIndex(m_Jiffies, 0);     // 查找当前轮的步长数，在上层处于的槽位
                TimerCascade(w1, w2.SoltArray[idx]);
                if (idx == 0)
                {
                    idx = CalcExpireWheelIndex(m_Jiffies, 1);
                    TimerCascade(w2, w3.SoltArray[idx]);
                    if (idx == 0)
                    {
                        idx = CalcExpireWheelIndex(m_Jiffies, 2);
                        TimerCascade(w3, w4.SoltArray[idx]);
                        if (idx == 0)
                        {
                            idx = CalcExpireWheelIndex(m_Jiffies, 3);
                            TimerCascade(w4, w5.SoltArray[idx]);
                        }
                    }
                }
            }
            else
            {
                var invokeSolt = w1.SoltArray[index - 1];
                while(invokeSolt.Count > 0)
                {
                    var task = invokeSolt.First;
                    invokeSolt.Remove(task);
                    bool loopInvoke = task.Value.Invoke();        // 执行最低颗粒度轮的过期槽位中的所有定时器任务
                    if (loopInvoke)
                    {
                        InternalAddTimer(task.Value);
                    }
                    else
                    {
                        // 清除过期task对象
                        task.Value.Recyle();
                        m_TaskInstancePool.Return(task.Value);
                    }
                }
            }

            m_Jiffies++;
        }
    }

    /*
        对某个轮中的槽位进行展开到下一个轮中的操作
        param tw：展开的目标轮
        param solt：当前需要展开的槽位
    */
    private void TimerCascade(TimerWheel tw, LinkedList<TimerTask> solt)
    {
        while(solt.Count > 0)
        {
            var task = solt.First;
            solt.Remove(task);
            TimerAddToWheel(tw, task.Value);
        }
    }

    /*
        针对某个目标轮，插入定时器
        param tw：插入目标轮
        param task：定时器
    */
    private void TimerAddToWheel(TimerWheel tw, TimerTask task)
    {
        var expire = task.ExpireTime;
        long idx = expire - m_Jiffies;
        LinkedList<TimerTask> targetSolt = null;
        if (idx < k_TWRSize)
        {
            targetSolt = tw.SoltArray[CalcExpireRWheelIndex(expire) - 1];
        }
        else
        {
            for (int i = 0; i < k_TWLevel - 1; i++)
            {
                if (idx < 1 << k_TWRBits + (i + 1) * k_TWNBits)
                {
                    targetSolt = tw.SoltArray[CalcExpireWheelIndex(expire, i) - 1];
                    break;
                }
            }
        }
        targetSolt.AddLast(task);
        task.UpdateSoltInfo(targetSolt, targetSolt.Last);
    }

    /*
        计算过期时间在某个轮当前处于的下标
        （用位运算提取出某个位下的值）
        param expireTime：当前需要计算下标的过期时间
        param n：该轮所处于的层级
    */
    private int CalcExpireWheelIndex(long expireTime, int n)
    {
        return (int)(expireTime >> (k_TWRBits + (n * k_TWNBits)) & k_TWNMask);
    }

    // 计算某个过期时间在第一个轮中当前处于的下标
    private int CalcExpireRWheelIndex(long expireTime)
    {
        return (int)(expireTime & k_TWRMask);
    }

    // 新增一个定时器任务到时间轮中，外部新增定时器时调用该方法（区别展开时的针对某轮新增定时器的方法）
    private void InternalAddTimer(TimerTask timer)
    {
        var expireTime = timer.ExpireTime;
        var idx = expireTime - m_Jiffies;      // 定时器超时时间差
        LinkedList<TimerTask> targetSolt;
        if (idx < k_TWRSize)
        {
            targetSolt = m_TWArray[0].SoltArray[CalcExpireRWheelIndex(expireTime) - 1];
        }
        else if (idx < 0)
        {
            // 可能是加入了一个在当前时间之前的定时器，或者当前设置的定时器过期时间与jiffies相等
            targetSolt = m_TWArray[0].SoltArray[CalcExpireRWheelIndex(m_Jiffies) - 1];
        }
        else if (idx < 1 << (k_TWRBits + k_TWNBits))
        {
            var i = (expireTime >> k_TWRBits) & k_TWNMask;
            targetSolt = m_TWArray[1].SoltArray[i];
        }
        else if (idx < 1 << (k_TWRBits + 2 * k_TWNBits))
        {
            var i = (expireTime >> (k_TWRBits + k_TWNBits)) & k_TWNMask;
            targetSolt = m_TWArray[2].SoltArray[i];
        }
        else if (idx < 1 << (k_TWRBits + 3 * k_TWNBits))
        {
            var i = (expireTime >> k_TWRBits + 2 * k_TWNBits) & k_TWNMask;
            targetSolt = m_TWArray[3].SoltArray[i];
        }
        else
        {
            // 定时器过期时间超过该时间轮的最大限制
            if (idx > 0xffffffffL)
            {
                idx = 0xffffffffL;
                expireTime = m_Jiffies + idx;
            }

            var i = (expireTime >> k_TWRBits + 3 * k_TWNBits) & k_TWNMask;
            targetSolt = m_TWArray[4].SoltArray[i];
        }
        
        targetSolt.AddLast(timer);
        timer.UpdateSoltInfo(targetSolt, targetSolt.Last);
    }

    #region 外部接口

    // 启动时间轮转动
    public void Run()
    {
        if (!m_IsRunning)
        {
            InitTimerWheels();
            m_IsRunning = true;
        }
    }

    /*
        更新时间轮进度，由外部推动
        param userTick：外部指定当前间隔内走过的tick步长
    */
    public void UpdateProcess(int userTick = 1)
    {
        if (userTick <= 0)
            throw new ArgumentException("时间轮推进长度tick数值不合法");

        if (!m_IsRunning) return;

        TimerUpdate(userTick);
    }

    /*
        新增一个定时器
        param interval：时间间隔，单位：秒
        param loopTimes：执行次数，0则视为执行1次
        param callback：任务回调
        param userData：回调用户自定义参数
    */
    public TimerTask AddTimer(float interval, int loopTimes = -1, Action<object[]> callback = null, object[] userData = null)
    {
        if (interval <= 0)
            throw new ArgumentException("定时器过期时间跨度不能为0或负数");

        if (loopTimes != -1 && loopTimes < 0)
            throw new ArgumentException("定时器执行次数参数异常，可选-1或者大于0的整形");

        var timerTask = m_TaskInstancePool.Get();
        timerTask.InitTimer(interval, 0, loopTimes, callback, userData);
        InternalAddTimer(timerTask);
        return timerTask;
    }

    /*
        延迟执行某个任务
        param delay：延迟间隔，只执行一次。单位：秒
        param callback：任务回调
        param userData：回调用户自定义参数
    */
    public void SetDelay(float delay, Action<object[]> callback, object[] userData = null)
    {
        if (delay <= 0)
            throw new ArgumentException("延迟间隔参数不合法");

        AddTimer(delay, 0, callback, userData);
    }

    /*
        删除某个定时器
    */
    public void RemoveTimer(TimerTask task)
    {
        if (task is null)
            throw new ArgumentNullException("无效定时器对象");

        if (task.RemoveSelf())          // 内部检测合法性，外部无需关注该对象是否仍然有效
        {
            m_TaskInstancePool.Return(task);
        }
    }

    /*
        修改定时器的属性
        param interval：修改时间间隔
        param loopTimes：修改执行次数
        param callback：修改任务回调
        param userData：修改回调用户自定义参数
    */
    public void ModifyTimer(TimerTask task, float interval, int loopTimes, Action<object[]> callback = null, object[] userData = null)
    {
        if (task is null)
            throw new ArgumentNullException("无效定时器对象");

        if (interval <= 0)
            throw new ArgumentException("定时器过期时间跨度不能为0或负数");
        
        if (loopTimes != -1 && loopTimes < 0)
            throw new ArgumentException("定时器执行次数参数异常，可选-1或者大于0的整形");
        
        task.ModifyTask(interval, loopTimes, callback, userData);
    }

    #endregion
}
