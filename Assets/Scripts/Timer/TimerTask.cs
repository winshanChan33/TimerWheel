/*
    定时器任务类

    内部只关注tick，时间运算在外部进行
*/

using System;

public class TimerTask
{
    private Action<object[]> m_Callback;    // 单个定时器任务的回调方法
    private object[] m_UserData;            // 用户自定义回调数据

    // 定时器唯一ID
    public int ID { get; private set; }
    // 定时器的到期tick
    public long Expire { get; private set; }
    // 失效标记
    public bool IsValid { get; set; }
    // 当前所处槽位下标
    public int SoltIndex { get; set; }

    private long m_Delay;                   // 延迟tick
    private int m_Times;                    // 执行次数
    private int m_CurInvokeTimes;

    /*
        param nowTick：定时器当前tick
        param delayTick：延迟tick
        param times：执行次数，-1则是循环执行，0默认为执行1次，>1则该定时器执行n次
        param callback：执行回调
        param userData：用户自定义数据数组
    */
    public void Init(int id, long nowTick, long delayTick = 0, int times = 0, Action<object[]> callback = null, object[] userData = null)
    {
        ID = id;
        Expire = nowTick + delayTick;
        m_Delay = delayTick;
        m_Times = times;
        m_CurInvokeTimes = 0;

        m_Callback = callback;
        m_UserData = userData;

        IsValid = true;
    }

    public void Reset()
    {
        SoltIndex = -1;
        Expire = 0;
        IsValid = false;
        m_Callback = null;
        m_UserData = null;
        m_Delay = 0;
        m_Times = 1;
    }

    // 更新到下一次过期tick
    public void UpdateNextExpire(long nowTick)
    {
        Expire = nowTick + m_Delay;
    }

    // 定时器过时后，执行定时器任务
    public bool Invoke()
    {
        m_Callback?.Invoke(m_UserData);
        m_CurInvokeTimes++;
        return CheckInkoveTimes();;
    }

    private bool CheckInkoveTimes()
    {
        if (m_Times <= 1 && m_Times >= 0)       // 此处loopTimes不可能为负数，在外部做传参校验即可
            return false;
        
        return m_Times == -1 || m_CurInvokeTimes <= m_Times;
    }

    public void ModifyTimer(int times, Action<object[]> callback, object[] userData)
    {
        // 标志该定时器失效了
        if (!IsValid)
            return;

        if ((times != 0 && times > m_Times) || times == -1)
        {
            m_Times = times;
            m_CurInvokeTimes = 0;       // 清空记录的执行次数，重新计算
        }

        m_Callback = callback;
        m_UserData = userData;
    }
}
