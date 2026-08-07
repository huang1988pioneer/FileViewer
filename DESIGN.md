# Design

## Mood

安靜明亮的 Windows 工作桌：白色檯面、深苔綠的可靠標記、檔案內容優先。

## Color strategy

Restrained. 純白背景與低彩度面板承擔結構；苔綠只用於目前位置、主操作與成功狀態。

```css
:root {
  --bg: oklch(1 0 0);
  --surface: oklch(0.975 0.004 150);
  --surface-strong: oklch(0.945 0.008 150);
  --ink: oklch(0.22 0.018 155);
  --muted: oklch(0.49 0.024 155);
  --primary: oklch(0.42 0.12 140);
  --accent: oklch(0.58 0.14 232);
  --line: oklch(0.88 0.012 155);
}
```

## Typography

使用 Segoe UI Variable／Segoe UI；標題清楚但不誇張，資料列採緊湊的固定字級。

## Components

12px 圓角面板、4px 控制項圓角、清楚的選取底色與 1px 邊界。主要按鈕以苔綠實色與白字呈現。
