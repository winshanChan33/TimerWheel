/*
    定时器管理器，用于驱动时间轮的推进，和提供外部接口
*/

using System;
using System.Collections;
    
public class TimerManager
{
    public const int k_TWRootBits = 8;
    public const int k_TWNormalBits = 6;
    public const int k_TWRSize = 1 >> k_TWRootBits;        // 第一层时间轮的槽位个数
    public const int k_TWNSize = 1 >> k_TWNormalBits;      // 第二层时间轮的槽位个数
    public const int k_TWRMask = k_TWRSize - 1;            // 位操作掩码，用于计算当前定时器应该加入到哪个槽中
    public const int k_TWNMask = k_TWNSize - 1;

    public static TimerManager Instance { get; } = new TimerManager();

    private const int HZ = 10;       // 最小层级tick一次耗时，精确到毫秒。如：10ms
    private const int k_TWLevel = 5;    // 层级轮层级个数

    private TimerWheel[] m_TWArray;      // 存储所有的时间轮
    private long m_Jiffies = 0;         // 当前走到的时间点，此前时间点的定时器全部执行完毕
    private bool m_IsRunning = false;

    public TimerManager()
    {

    }

    private void InitTimerWheels()
    {
        m_TWArray = new TimerWheel[k_TWLevel];
        m_TWArray[0] = new TimerWheel(k_TWRSize);
        for (int i = 1; i < k_TWLevel - 1; i++)
        {
            m_TWArray[i] = new TimerWheel(k_TWNSize);
        }
    }

    // 指针推进往前走一步
    private void TimerTick(TimerWheel tw)
    {
        tw.Tick++;
        var index = tw.Tick & k_TWRMask;
        if (index == 0)
        {
            int i = 0;
            
        }
    }

    /*
        计算某个轮当前走到的下标
        param expireTime：当前需要计算下标的过期时间
        param n：该轮所处于的层级
    */
    private int GetWheelCurIndex(long expireTime, int n)
    {
        return (int)(expireTime >> (k_TWRSize + (n * k_TWNSize)) & k_TWNMask);
    }

    // 新增一个定时器任务到时间轮中
    private void InternalAddTimer(TimerTask timer)
    {
        var expireTime = timer.ExpireTime;
        var diff = expireTime - m_Jiffies;      // 定时器超时时间差

    }

    /*
        对某个轮中的槽位进行展开到下一个轮中的操作
    */
    private void Cascade()
    {

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

    #endregion
}
