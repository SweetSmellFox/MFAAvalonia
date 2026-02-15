using Avalonia.Input;
using Avalonia.Interactivity;

namespace MFAAvalonia.Controls.Events;

public class DragTabDragStartedEventArgs : DragTabItemEventArgs
{
    public DragTabDragStartedEventArgs(DragTabItem dragTabItem, VectorEventArgs dragStartedEventArgs)
        : base(dragTabItem) { }

    public DragTabDragStartedEventArgs(RoutedEvent routedEvent, DragTabItem tabItem, VectorEventArgs dragStartedEventArgs)
        : base(routedEvent, tabItem) { }

    public DragTabDragStartedEventArgs(RoutedEvent routedEvent, Interactive source, DragTabItem tabItem, VectorEventArgs dragStartedEventArgs)
        : base(routedEvent, source, tabItem) { }
}
