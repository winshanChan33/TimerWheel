using UnityEngine;

public class MainEntrance : MonoBehaviour
{
    private int m_RunningTimerId;
    private void Awake()
    {
    }

    private void Start()
    {   
        // 开启定时器管理计算
        TimerManager.Instance.Init();
        Debug.Log("==========定时器开启运作===========");

        AddTimerTest();
        // RemoveTimerTest();
        // ModifyTimerTest();
        // TimerStressTest();
    }

    private void Update()
    {
        TimerManager.Instance.Update();
    }

    // 在运行期间随机插入大量定时器
    private void TimerStressTest()
    {
        // 每隔200ms就开启一个定时器
        TimerManager.Instance.AddMSTimer(200, -1, (_) => 
        {
            TimerManager.Instance.AddTimer(2);
        });

        // 延迟5s，一次性开启10w个定时器
        TimerManager.Instance.AddTimer(5, -1, (_) => 
        {
            for (int i = 0; i < 100000; i++)
            {
                TimerManager.Instance.AddTimer(2, -1);
            }
        });
    }

    // 新增定时器测试
    private void AddTimerTest()
    {
        m_RunningTimerId = TimerManager.Instance.AddMSTimer(20, -1, (userData) =>
        {
            Debug.Log(Time.realtimeSinceStartup + "：timer1：正在执行一个时间间隔为20ms的定时器，回调中带自定义参数：" + userData[0] + "，" + userData[1]);
        }, new object[2]{ 10, 12 });
    }

    private void RemoveTimerTest()
    {
        // 3s后删除上一个循环定时器
        TimerManager.Instance.AddTimer(3, 1, (_) => {
            TimerManager.Instance.RemoveTimer(m_RunningTimerId);
            Debug.Log("timer1：删除定时器");
        });
    }

    private void ModifyTimerTest()
    {
        var timerId = TimerManager.Instance.AddTimer(1, -1, (_) =>
        {
            Debug.Log("timer2：正在执行一个循环执行的定时器ModifyTimer");
        });
        TimerManager.Instance.AddTimer(5, 1, (_) => 
        {
            Debug.Log("timer3：延迟5s修改timer2的回调方法");
            TimerManager.Instance.ModifyTimer(timerId, 0, (_) =>
            {
                Debug.Log("timer2：timer2回调已经修改");
            });
        });
    }
}
