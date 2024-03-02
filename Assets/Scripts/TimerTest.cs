using System;
using UnityEngine;

public class MainEntrance : MonoBehaviour
{
    private float m_AccumTime = 0;      // 积累时间
    private DateTime m_LastTime;
    private void Awake()
    {
    }

    private void Start()
    {   
        m_LastTime = DateTime.Now;

        // 开启定时器管理计算
        TimerManager.Instance.Run();
        Debug.Log("==========定时器开启运作===========");

        AddTimerTest();
        RemoveTimerTest();
        ModifyTimerTest();

        // 定时器开启之后5s一次性加入大量定时器
        TimerManager.Instance.SetDelay(5, (_) => AddManyTimerOneTime());
    }

    private void Update()
    {
        if (!TimerManager.Instance.IsRunning)
            return;

        m_AccumTime += Time.deltaTime;      // 记录上一帧到当前帧的时间间隔。单位：秒
        if (m_AccumTime >= 1)
        {
            TimerManager.Instance.UpdateProcess((int)(m_AccumTime * 1000));
            m_AccumTime = 0;
        }

        // 在运行期间随机插入定时器
        if ((DateTime.Now - m_LastTime).TotalSeconds > UnityEngine.Random.Range(1, 5))
        {
            m_LastTime = DateTime.Now;
            TimerManager.Instance.AddTimer(5, 0);
        }
    }

    // 同一个时刻加入大量定时器
    private void AddManyTimerOneTime()
    {
        for (int i = 0; i < 1000000; i++)
        {
            TimerManager.Instance.SetDelay(1, (_) => {});
        }
    }

    // 新增定时器测试
    private void AddTimerTest()
    {
        TimerManager.Instance.AddTimer(1, -1, (userData) =>
        {
            Debug.Log("正在执行一个时间间隔为1s的定时器，回调中带自定义参数：" + userData[0] + "，" + userData[1]);
        }, new object[2]{ 10, 12 });
    }

    private void RemoveTimerTest()
    {
        var timerTask = TimerManager.Instance.AddTimer(2, -1, (_) =>
        {
            Debug.Log("正在执行一个定时器，在10s之后会被删除");
        });
        TimerManager.Instance.SetDelay(10, (_) => {
            TimerManager.Instance.RemoveTimer(timerTask);
        });
    }

    private void ModifyTimerTest()
    {
        var timerTask = TimerManager.Instance.AddTimer(5, -1, (_) =>
        {
            Debug.Log("正在执行一个定时器ModifyTimer，在20s之后会被修改属性");
        });
        TimerManager.Instance.SetDelay(20, (_) => TimerManager.Instance.ModifyTimer(timerTask, 8, 20));
    }
}
