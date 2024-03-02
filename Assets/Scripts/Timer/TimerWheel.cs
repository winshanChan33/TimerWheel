/*
    时间轮结构
*/

using System.Collections.Generic;

public class TimerWheel
{
    // 单个时间轮的槽位数组，由soltSize个双向链表组成
    private LinkedList<TimerTask>[] m_SoltArray;
    public LinkedList<TimerTask>[] SoltArray
    {
        get { return m_SoltArray; }
        set { m_SoltArray = value; }
    }

    // param soltSize：槽位总数
    public TimerWheel(int soltSize)
    {
        m_SoltArray = new LinkedList<TimerTask>[soltSize];
        for (int i = 0; i < soltSize; i++)
        {
            m_SoltArray[i] = new();
        }
    }
}
