/*
    定时器任务类
*/

using System;

public class TimerTask
{
    private Action<object[]> m_Callback;    // 单个定时器任务的回调方法
    private object[] m_UserData;            // 用户自定义回调数据

    private long m_ExpireTime;              // 定时器的到期时间
    public long ExpireTime
    {
        get { return m_ExpireTime; }
    }
    
    public TimerTask()
    {

    }

    // 定时器过时后，执行定时器任务
    public void Invoke()
    {
        m_Callback?.Invoke(m_UserData);
    }
}
