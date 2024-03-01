/*
    时间轮结构
*/

using System.Collections.Generic;

public class TimerWheel
{
    // 单个时间轮的槽位数组，由soltSize个双向链表组成
    private LinkedList<TimerTask>[] m_SoltArray;

    private int m_CurTick;          // 当前指针位置
    public int Tick
    {
        get { return m_CurTick; }
        set { m_CurTick = value; }
    }

    // param soltSize：槽位总数
    public TimerWheel(int soltSize)
    {
        m_SoltArray = new LinkedList<TimerTask>[soltSize];
    }
}
