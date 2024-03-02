/*
    简单对象池
*/

using System.Collections.Generic;

public class ObjectPool<T> where T : new()
{
    private const int k_MaxCnt = 1 << 8;
    private readonly Stack<T> m_Objects;

    public ObjectPool()
    {
        m_Objects = new Stack<T>();
    }

    public T Get()
    {
        if (m_Objects.Count > 0)
        {
            return m_Objects.Pop();
        }

        return new T();
    }

    public void Return(T obj)
    {
        if (m_Objects.Count < k_MaxCnt)
            m_Objects.Push(obj);
    }
}
