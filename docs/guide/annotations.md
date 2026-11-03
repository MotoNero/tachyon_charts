# Annotations

In XAML they live under `Chart.Annotations`:

```xml
<t:HorizontalLineAnnotation Y="1.5" />
<t:VerticalLineAnnotation X="3" />
<t:BoxAnnotation />
<t:TextAnnotation X="3" Y="1.5" Text="Start" />
```

The positions are bindings like any other. With the `AnnotationEdit` block they can be moved by mouse, and the new values arrive in the binding.

Annotations also appear in every [export](export.md).
