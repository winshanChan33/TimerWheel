/*
    简单对象池
*/

using System.Collections.Generic;

public class ObjectPool<T> where T : new()
{
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
        m_Objects.Push(obj);
    }
}
