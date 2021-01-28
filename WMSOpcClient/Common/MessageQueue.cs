using System;
using System.Collections.Generic;
using System.Text;

namespace WMSOpcClient.Common
{
    public class MessageQueue<T> : Queue<T>
    {
        //  Add an event that will be triggered after adding an item
        public event Action<T> ItemAdded;

        //  Override the add method
        public new void Enqueue(T obj)
        {
            //  Call the parent add action
            base.Enqueue(obj);

            //  Trigger the event
            ItemAdded?.Invoke(obj);
        }
    }
}
