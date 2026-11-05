using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using static Microsoft.UI.Reactor.Factories;

namespace Windtranslator;

// Recreates SavedTextItemStyle while leaving list activation, focus and keyboard
// handling with the native ListViewItem. Only transient pointer visuals live here.
internal static class SavedTextItems
{
    internal static Style CreateContainerStyle() => new(typeof(ListViewItem))
    {
        Setters =
        {
            new Setter(Control.PaddingProperty, new Thickness(0)),
            new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
        },
    };

    internal static Element Card(Element content) =>
        Component<SavedTextCardView, SavedTextCardProps>(new(content));
}

internal sealed record SavedTextCardProps(Element Content);

internal sealed class SavedTextCardView : Component<SavedTextCardProps>
{
    private Action<bool>? _setPressed;

    public override Element Render()
    {
        var (isHovered, setHovered) = UseState(false);
        var (isPressed, setPressed) = UseState(false);
        _setPressed = setPressed;

        var background = isPressed
            ? Theme.Ref("SubtleFillColorTertiaryBrush")
            : isHovered ? Theme.SubtleFill : Theme.Ref("WindCardBackgroundBrush");

        return Border(Props.Content)
            .Margin(0, 0, 0, 8)
            .Padding(12)
            .Background(background)
            .WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
            .CornerRadius(8)
            .OnPointerEntered((_, _) => setHovered(true))
            .OnPointerExited((_, _) =>
            {
                setHovered(false);
                setPressed(false);
            })
            .OnPointerPressed((_, _) => setPressed(true))
            .OnMountAdd(AttachPointerHandlers)
            .OnUnmountAdd(DetachPointerHandlers);
    }

    private void AttachPointerHandlers(FrameworkElement element)
    {
        // Receive release even if a nested action button handled the event.
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndPress), true);
        element.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(EndPress), true);
        element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(EndPress), true);
    }

    private void DetachPointerHandlers(FrameworkElement element)
    {
        element.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndPress));
        element.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(EndPress));
        element.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(EndPress));
        _setPressed = null;
    }

    private void EndPress(object sender, PointerRoutedEventArgs args) => _setPressed?.Invoke(false);
}
