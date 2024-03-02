/*
    定时器任务类
*/

using System;
using System.Collections.Generic;

public class TimerTask
{
    private Action<object[]> m_Callback;    // 单个定时器任务的回调方法
    private object[] m_UserData;            // 用户自定义回调数据

    private long m_ExpireTime;              // 定时器的到期时间
    public long ExpireTime
    {
        get { return m_ExpireTime; }
    }
    private long m_Interval;            // 毫秒
    private int m_LoopTimes;
    private int m_CurInvokeTimes;

    // 记录当前的槽位信息，用于删除定时器
    private LinkedList<TimerTask> m_CurSolt;
    private LinkedListNode<TimerTask> m_CurLinkNode;
    
    /*
        param interval：定时器间隔，单位：秒
        param delay：延迟定时器开启间隔，单位：秒
        param loopTimes：执行次数，-1则是循环执行，0默认为执行1次，>1则该定时器执行n次
        param callback：执行回调
        param userData：用户自定义数据数组
    */
    public void InitTimer(float interval, float delay = 0, int loopTimes = 0, Action<object[]> callback = null, object[] userData = null)
    {
        m_Interval = (long)(interval * 1000);
        m_ExpireTime = TimerManager.Instance.Jiffies + (long)((interval + delay) * 1000);
        m_LoopTimes = loopTimes;
        m_CurInvokeTimes = 0;

        m_Callback = callback;
        m_UserData = userData;
    }

    public void Recyle()
    {
        m_CurSolt = null;
        m_CurLinkNode = null;
    }

    // 定时器过时后，执行定时器任务
    public bool Invoke()
    {
        m_Callback?.Invoke(m_UserData);
        m_CurInvokeTimes++;
        m_ExpireTime = TimerManager.Instance.Jiffies + m_Interval;
        return CheckLoop();;
    }

    private bool CheckLoop()
    {
        if (m_LoopTimes <= 1 && m_LoopTimes >= 0)       // 此处loopTimes不可能为负数，在外部做传参校验即可
            return false;
        
        return m_LoopTimes == -1 || m_CurInvokeTimes <= m_LoopTimes;
    }

    // 每次加入到槽位链表的时候，记录相关信息
    public void UpdateSoltInfo(LinkedList<TimerTask> solt, LinkedListNode<TimerTask> node)
    {
        m_CurSolt = solt;
        m_CurLinkNode = node;
    }

    public bool RemoveSelf()
    {
        if (!IsValid())
            return false;
        
        m_CurSolt.Remove(m_CurLinkNode);
        Recyle();
        return true;
    }

    public void ModifyTask(float interval, int loopTimes, Action<object[]> callback, object[] userData)
    {
        if (!IsValid())
            return;     // 标志该定时器失效了

        m_Interval = (long)(interval * 1000);
        m_LoopTimes = loopTimes;
        if (callback != null)
        {
            // 回调与参数需要同时修改
            m_Callback = callback;
            m_UserData = userData;
        }
    }

    public bool IsValid()
    {
        if (m_CurSolt is null || m_CurLinkNode is null)
            return false;
        
        return true;
    }
}
