/*
    时间轮的单个槽位，由1个双向链表组成
*/

using System.Collections.Generic;

public class TimerSolt
{
    public LinkedList<TimerTask> SoltTasks { get; set; } = new();
}
