using Avalonia.Input;
using Avalonia.Interactivity;

namespace MFAAvalonia.Controls.Events;

public class DragTabDragCompletedEventArgs : DragTabItemEventArgs
{
    public DragTabDragCompletedEventArgs(DragTabItem dragItem, VectorEventArgs dragCompletedEventArgs)
        : base(dragItem) { }

    public DragTabDragCompletedEventArgs(RoutedEvent routedEvent, DragTabItem dragItem, VectorEventArgs dragCompletedEventArgs)
        : base(routedEvent, dragItem) { }

    public DragTabDragCompletedEventArgs(RoutedEvent routedEvent, Interactive source, DragTabItem dragItem, VectorEventArgs dragCompletedEventArgs)
        : base(routedEvent, source, dragItem) { }
}
