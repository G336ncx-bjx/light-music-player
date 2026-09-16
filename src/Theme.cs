using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace LightMusic
{
    /// <summary>配色与样式（深色 / 浅色两套）。</summary>
    public static class Theme
    {
        public static string Current = "dark";

        private static readonly string[] DarkKeys = new string[]
        {
            "Window", "Panel", "Card", "Hover", "Pressed", "Selected", "Border",
            "Text", "TextDim", "TextMuted", "Accent", "AccentHover", "AccentPressed",
            "AccentSoft", "OnAccent", "Danger", "TrackBg", "ScrollThumb", "ScrollThumbHover",
            "MenuBg", "TooltipBg"
        };

        private static readonly string[] DarkValues = new string[]
        {
            "#0E1014", "#13151B", "#181B22", "#212531", "#2A2F3D", "#242C42", "#252A35",
            "#E9ECF2", "#A7AEBC", "#6F7787", "#6D8BFF", "#7E99FF", "#5F7DF0",
            "#1E2740", "#FFFFFF", "#F87171", "#2A2F3D", "#3A4150", "#4C5566",
            "#1B1F27", "#1B1F27"
        };

        private static readonly string[] LightValues = new string[]
        {
            "#F5F6FA", "#FFFFFF", "#FFFFFF", "#ECEFF5", "#E0E5EE", "#E6EBFB", "#E3E7EE",
            "#171A21", "#5A6272", "#8A91A0", "#4F6DF5", "#6079F7", "#4460E0",
            "#E8EDFE", "#FFFFFF", "#DC2626", "#DDE2EA", "#C6CCD8", "#AEB6C4",
            "#FFFFFF", "#FFFFFF"
        };

        private static bool _stylesLoaded;

        public static void EnsureStyles()
        {
            if (_stylesLoaded) return;
            _stylesLoaded = true;

            Apply(Current);

            Stream stream = typeof(Theme).Assembly.GetManifestResourceStream("LightMusic.Theme.xaml");
            if (stream == null) throw new InvalidOperationException("缺少内嵌样式资源 LightMusic.Theme.xaml");
            LoadResource(stream);

            Stream templates = typeof(Theme).Assembly.GetManifestResourceStream("LightMusic.Templates.xaml");
            if (templates != null) LoadResource(templates);
        }

        private static void LoadResource(Stream stream)
        {
            using (stream)
            {
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    ResourceDictionary dict = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(reader);
                    foreach (object key in dict.Keys)
                    {
                        Application.Current.Resources[key] = dict[key];
                    }
                }
            }
        }

        public static void Apply(string name)
        {
            Current = name == "light" ? "light" : "dark";
            string[] values = Current == "light" ? LightValues : DarkValues;
            for (int i = 0; i < DarkKeys.Length; i++)
            {
                Color color = ParseColor(values[i]);
                SolidColorBrush brush = new SolidColorBrush(color);
                brush.Freeze();
                Application.Current.Resources[DarkKeys[i]] = brush;
            }
        }

        public static void Toggle()
        {
            Apply(Current == "dark" ? "light" : "dark");
        }

        private static Color ParseColor(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }

    /// <summary>矢量图标（24x24 画布）。</summary>
    public static class Icons
    {
        private class Def
        {
            public string Data;
            public bool Filled;
            public double Thickness;

            public Def(string data, bool filled, double thickness)
            {
                Data = data;
                Filled = filled;
                Thickness = thickness;
            }
        }

        private static readonly Dictionary<string, Def> Map = Build();

        private static Dictionary<string, Def> Build()
        {
            Dictionary<string, Def> m = new Dictionary<string, Def>(StringComparer.OrdinalIgnoreCase);
            m["play"] = new Def("M8.4 5.4 L18.8 12 L8.4 18.6 Z", true, 0);
            m["pause"] = new Def("M8.6 5.4 H11.4 V18.6 H8.6 Z M12.6 5.4 H15.4 V18.6 H12.6 Z", true, 0);
            m["prev"] = new Def("M6.6 5.4 H8.8 V18.6 H6.6 Z M18.6 5.4 L10.4 12 L18.6 18.6 Z", true, 0);
            m["next"] = new Def("M17.4 5.4 H15.2 V18.6 H17.4 Z M5.4 5.4 L13.6 12 L5.4 18.6 Z", true, 0);
            m["stop"] = new Def("M7 7 H17 V17 H7 Z", true, 0);
            m["music"] = new Def("M9.8 18.4 V8.4 L18 6.8 V16.8 M9.8 18.4 A2.6 2.6 0 1 1 4.6 18.4 A2.6 2.6 0 0 1 9.8 18.4 "
                + "M18 16.8 A2.6 2.6 0 1 1 12.8 16.8 A2.6 2.6 0 0 1 18 16.8", false, 1.8);
            m["library"] = new Def("M4 6.6 H20 M4 12 H20 M4 17.4 H12.5", false, 1.8);
            m["queue"] = new Def("M4 6.6 H20 M4 12 H20 M4 17.4 H12.5 M17.5 14.4 V20.4 M14.5 17.4 H20.5", false, 1.8);
            m["lyrics"] = new Def("M4.6 5.6 H19.4 V15.6 H11 L6.4 19.8 V15.6 H4.6 Z M8.2 9 H15.8 M8.2 12.2 H13", false, 1.7);
            m["search"] = new Def("M10.6 4.2 A6.4 6.4 0 1 0 10.6 17 A6.4 6.4 0 1 0 10.6 4.2 Z "
                + "M15.2 15.2 L20.6 20.6", false, 1.8);
            m["folder"] = new Def("M3.6 7.4 A2 2 0 0 1 5.6 5.4 H9.4 L11.4 7.8 H18.4 A2 2 0 0 1 20.4 9.8 V17 "
                + "A2 2 0 0 1 18.4 19 H5.6 A2 2 0 0 1 3.6 17 Z", false, 1.7);
            m["settings"] = new Def("M4 8 H12.6 M16.6 8 H20 M4 16 H8.6 M12.6 16 H20 "
                + "M14.6 6 A2 2 0 1 0 14.6 10 A2 2 0 1 0 14.6 6 Z "
                + "M10.6 14 A2 2 0 1 0 10.6 18 A2 2 0 1 0 10.6 14 Z", false, 1.7);
            m["theme"] = new Def("M12 7.6 A4.4 4.4 0 1 0 12 16.4 A4.4 4.4 0 1 0 12 7.6 Z "
                + "M12 3 V4.6 M12 19.4 V21 M3 12 H4.6 M19.4 12 H21 "
                + "M5.6 5.6 L6.8 6.8 M17.2 17.2 L18.4 18.4 M18.4 5.6 L17.2 6.8 M6.8 17.2 L5.6 18.4", false, 1.6);
            m["monitor"] = new Def("M3.4 5.6 H20.6 V15.4 H3.4 Z M8.6 19.8 H15.4 M12 15.4 V19.8", false, 1.7);
            m["lock"] = new Def("M7.6 11 V8.6 A4.4 4.4 0 0 1 16.4 8.6 V11 M5.6 11 H18.4 V19.8 H5.6 Z", false, 1.7);
            m["unlock"] = new Def("M7.6 11 V8.6 A4.4 4.4 0 0 1 16.4 8.6 M5.6 11 H18.4 V19.8 H5.6 Z", false, 1.7);
            m["volume"] = new Def("M4 9.6 H7.4 L11.2 6.6 V17.4 L7.4 14.4 H4 Z M14.6 9.2 A4.4 4.4 0 0 1 14.6 14.8 "
                + "M17.2 6.8 A7.8 7.8 0 0 1 17.2 17.2", false, 1.7);
            m["mute"] = new Def("M4 9.6 H7.4 L11.2 6.6 V17.4 L7.4 14.4 H4 Z M14.8 9.6 L19.6 14.4 M19.6 9.6 L14.8 14.4", false, 1.7);
            m["plus"] = new Def("M12 5.6 V18.4 M5.6 12 H18.4", false, 1.9);
            m["trash"] = new Def("M5.6 7.6 H18.4 M9.6 7.6 V5.4 H14.4 V7.6 M7.6 7.6 L8.4 19.6 H15.6 L16.4 7.6 "
                + "M10.6 10.6 V16.6 M13.4 10.6 V16.6", false, 1.6);
            m["refresh"] = new Def("M19.6 12 A7.6 7.6 0 1 1 16.9 6.2 M19.9 3.8 V8.4 H15.3", false, 1.8);
            m["close"] = new Def("M6.6 6.6 L17.4 17.4 M17.4 6.6 L6.6 17.4", false, 1.9);
            m["check"] = new Def("M5.4 12.6 L10 17.2 L18.8 7", false, 2.0);
            m["back"] = new Def("M14.6 6 L8.6 12 L14.6 18", false, 1.9);
            m["chevron-right"] = new Def("M9.6 6 L15.6 12 L9.6 18", false, 1.8);
            m["chevron-down"] = new Def("M6.4 9.6 L12 15.2 L17.6 9.6", false, 1.8);
            m["minimize"] = new Def("M5.6 12 H18.4", false, 1.9);
            m["maximize"] = new Def("M6 6.4 H18 V18.4 H6 Z", false, 1.7);
            m["shuffle"] = new Def("M4 7.4 H7.2 L16.4 17 H20 M20 17 L17.6 14.8 M20 17 L17.6 19.2 "
                + "M4 17 H7.2 L10.4 13.6 M14.6 9.4 L16.4 7.4 H20 M20 7.4 L17.6 5.2 M20 7.4 L17.6 9.6", false, 1.7);
            m["repeat"] = new Def("M5.2 10.6 C5.2 7.6 7.8 5.6 12 5.6 H17.6 M17.6 3.6 L19.9 5.6 L17.6 7.6 "
                + "M18.8 13.4 C18.8 16.4 16.2 18.4 12 18.4 H6.4 M6.4 16.4 L4.1 18.4 L6.4 20.4", false, 1.7);
            m["repeat-one"] = new Def("M5.2 10.6 C5.2 7.6 7.8 5.6 12 5.6 H17.6 M17.6 3.6 L19.9 5.6 L17.6 7.6 "
                + "M18.8 13.4 C18.8 16.4 16.2 18.4 12 18.4 H6.4 M6.4 16.4 L4.1 18.4 L6.4 20.4 "
                + "M11 9.6 L12.6 8.6 V15.6", false, 1.6);
            m["sequential"] = new Def("M4 6.6 H12.4 M4 12 H9.6 M4 17.4 H12.4 M15.4 12 H20 "
                + "M20 12 L17.6 9.6 M20 12 L17.6 14.4", false, 1.7);
            m["clock"] = new Def("M12 4.4 A7.6 7.6 0 1 0 12 19.6 A7.6 7.6 0 1 0 12 4.4 Z "
                + "M12 7.6 V12.3 L15.4 14.2", false, 1.7);
            m["disc"] = new Def("M12 3.8 A8.2 8.2 0 1 0 12 20.2 A8.2 8.2 0 1 0 12 3.8 Z "
                + "M12 9.4 A2.6 2.6 0 1 0 12 14.6 A2.6 2.6 0 1 0 12 9.4 Z", false, 1.6);
            m["sort"] = new Def("M7 5.6 V18.4 M7 18.4 L4.4 15.8 M7 18.4 L9.6 15.8 M13 8.4 H20 M13 12.4 H17.6 M13 16.4 H15.2", false, 1.7);
            m["info"] = new Def("M12 4.4 A7.6 7.6 0 1 0 12 19.6 A7.6 7.6 0 1 0 12 4.4 Z "
                + "M12 11.2 V16 M12 7.6 V8", false, 1.7);
            m["play-circle"] = new Def("M12 3.8 A8.2 8.2 0 1 0 12 20.2 A8.2 8.2 0 1 0 12 3.8 Z "
                + "M10 8.4 L15.8 12 L10 15.6 Z", false, 1.6);
            m["queue-add"] = new Def("M4 6.6 H15 M4 12 H15 M4 17.4 H9.6 M17.4 13 V19.6 M14.1 16.3 H20.7", false, 1.7);
            m["heart"] = new Def("M12 19.4 C12 19.4 4.2 15.2 4.2 9.8 A4 4 0 0 1 12 7.6 A4 4 0 0 1 19.8 9.8 C19.8 15.2 12 19.4 12 19.4 Z", false, 1.7);
            m["folder-open"] = new Def("M3.4 8 H9.2 L11 10.2 H20.6 L18 19 H6 Z M3.4 8 V6.4 A1.6 1.6 0 0 1 5 4.8 H8.4 L10.2 7", false, 1.7);
            m["more"] = new Def("M5.4 10.8 A1.2 1.2 0 1 0 5.4 13.2 A1.2 1.2 0 1 0 5.4 10.8 Z "
                + "M12 10.8 A1.2 1.2 0 1 0 12 13.2 A1.2 1.2 0 1 0 12 10.8 Z "
                + "M18.6 10.8 A1.2 1.2 0 1 0 18.6 13.2 A1.2 1.2 0 1 0 18.6 10.8 Z", true, 0);
            m["dot"] = new Def("M12 9.6 A2.4 2.4 0 1 0 12 14.4 A2.4 2.4 0 1 0 12 9.6 Z", true, 0);
            return m;
        }

        /// <summary>生成一个 size x size 的图标画布（矢量，随主题换色）。</summary>
        public static Canvas Create(string name, double size, string brushKey)
        {
            Def def;
            if (!Map.TryGetValue(name, out def)) def = Map["info"];

            double scale = size / 24.0;
            System.Windows.Shapes.Path path = new System.Windows.Shapes.Path();
            path.Data = System.Windows.Media.Geometry.Parse(def.Data);
            path.Stretch = Stretch.None;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeMiterLimit = 2;
            path.RenderTransform = new ScaleTransform(scale, scale);

            if (def.Filled)
            {
                path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
            }
            else
            {
                path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
                path.StrokeThickness = def.Thickness / scale;
            }

            Canvas canvas = new Canvas();
            canvas.Width = size;
            canvas.Height = size;
            canvas.Children.Add(path);
            return canvas;
        }

        public static Brush Brush(string key)
        {
            object value = Application.Current.Resources[key];
            SolidColorBrush brush = value as SolidColorBrush;
            if (brush == null) return Brushes.White;
            return brush;
        }
    }

    /// <summary>界面构建小工具。</summary>
    public static class Ui
    {
        public static readonly FontFamily Font =
            new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Arial");

        public static void Bind(FrameworkElement element, DependencyProperty property, string key)
        {
            element.SetResourceReference(property, key);
        }

        public static TextBlock Text(string text)
        {
            return Text(text, 13, "Text", FontWeights.Normal);
        }

        public static TextBlock Text(string text, double size, string brushKey)
        {
            return Text(text, size, brushKey, FontWeights.Normal);
        }

        public static TextBlock Text(string text, double size, string brushKey, FontWeight weight)
        {
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = size;
            tb.FontWeight = weight;
            tb.FontFamily = Font;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            Bind(tb, TextBlock.ForegroundProperty, brushKey);
            return tb;
        }

        public static Button Button(string text, string styleKey, RoutedEventHandler click)
        {
            Button b = new Button();
            b.Content = Text(text, 13, "Text");
            b.Style = (Style)Application.Current.Resources[styleKey];
            if (click != null) b.Click += click;
            return b;
        }

        public static Button IconButton(string icon, double iconSize, string tooltip, RoutedEventHandler click)
        {
            Button b = new Button();
            b.Style = (Style)Application.Current.Resources["IconButton"];
            b.Content = Icons.Create(icon, iconSize, "TextDim");
            b.ToolTip = tooltip;
            if (click != null) b.Click += click;
            return b;
        }

        public static Button RoundButton(string icon, double iconSize, string tooltip, RoutedEventHandler click)
        {
            Button b = new Button();
            b.Style = (Style)Application.Current.Resources["RoundIconButton"];
            b.Content = Icons.Create(icon, iconSize, "TextDim");
            b.ToolTip = tooltip;
            if (click != null) b.Click += click;
            return b;
        }

        public static Border Card(params UIElement[] children)
        {
            Border border = new Border();
            border.Style = (Style)Application.Current.Resources["CardBox"];
            StackPanel panel = new StackPanel();
            foreach (UIElement child in children) panel.Children.Add(child);
            border.Child = panel;
            return border;
        }

        public static Grid Columns(params GridLength[] widths)
        {
            Grid grid = new Grid();
            foreach (GridLength w in widths)
            {
                ColumnDefinition cd = new ColumnDefinition();
                cd.Width = w;
                grid.ColumnDefinitions.Add(cd);
            }
            return grid;
        }

        public static GridLength Stars(double value)
        {
            return new GridLength(value, GridUnitType.Star);
        }

        public static GridLength Px(double value)
        {
            return new GridLength(value, GridUnitType.Pixel);
        }

        public static StackPanel Row(double spacing, params UIElement[] children)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < children.Length; i++)
            {
                if (i > 0 && spacing > 0) children[i].SetValue(FrameworkElement.MarginProperty,
                    new Thickness(spacing, 0, 0, 0));
                panel.Children.Add(children[i]);
            }
            return panel;
        }

        public static StackPanel Column(double spacing, params UIElement[] children)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Vertical;
            for (int i = 0; i < children.Length; i++)
            {
                if (i > 0 && spacing > 0) children[i].SetValue(FrameworkElement.MarginProperty,
                    new Thickness(0, spacing, 0, 0));
                panel.Children.Add(children[i]);
            }
            return panel;
        }
    }
}
