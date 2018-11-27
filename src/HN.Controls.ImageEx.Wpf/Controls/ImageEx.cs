﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HN.Pipes;
using HN.Services;
using Polly;

namespace HN.Controls
{
    [TemplatePart(Name = ImageTemplateName, Type = typeof(Image))]
    [TemplatePart(Name = FailedContentHostTemplateName, Type = typeof(ContentPresenter))]
    [TemplatePart(Name = LoadingContentHostTemplateName, Type = typeof(ContentPresenter))]
    [TemplateVisualState(GroupName = ImageStateGroupName, Name = NormalStateName)]
    [TemplateVisualState(GroupName = ImageStateGroupName, Name = OpenedStateName)]
    [TemplateVisualState(GroupName = ImageStateGroupName, Name = FailedStateName)]
    [TemplateVisualState(GroupName = ImageStateGroupName, Name = LoadingStateName)]
    public class ImageEx : Control
    {
        public static readonly DependencyProperty EnableLazyLoadingProperty = DependencyProperty.Register(nameof(EnableLazyLoading), typeof(bool), typeof(ImageEx), new PropertyMetadata(default(bool)));
        public static readonly DependencyProperty FailedTemplateProperty = DependencyProperty.Register(nameof(FailedTemplate), typeof(DataTemplate), typeof(ImageEx), new PropertyMetadata(default(DataTemplate)));
        public static readonly DependencyProperty FailedTemplateSelectorProperty = DependencyProperty.Register(nameof(FailedTemplateSelector), typeof(DataTemplateSelector), typeof(ImageEx), new PropertyMetadata(default(DataTemplateSelector)));
        public static readonly DependencyProperty IsLoadingProperty;
        public static readonly DependencyProperty LoadingTemplateProperty = DependencyProperty.Register(nameof(LoadingTemplate), typeof(DataTemplate), typeof(ImageEx), new PropertyMetadata(default(DataTemplate)));
        public static readonly DependencyProperty LoadingTemplateSelectorProperty = DependencyProperty.Register(nameof(LoadingTemplateSelector), typeof(DataTemplateSelector), typeof(ImageEx), new PropertyMetadata(default(DataTemplateSelector)));
        public static readonly DependencyProperty RetryCountProperty = DependencyProperty.Register(nameof(RetryCount), typeof(int), typeof(ImageEx), new PropertyMetadata(default(int)));
        public static readonly DependencyProperty RetryDelayProperty = DependencyProperty.Register(nameof(RetryDelay), typeof(TimeSpan), typeof(ImageEx), new PropertyMetadata(TimeSpan.Zero));
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(object), typeof(ImageEx), new PropertyMetadata(default(object), OnSourceChanged));
        public static readonly DependencyProperty StretchDirectionProperty = DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(ImageEx), new PropertyMetadata(StretchDirection.Both));
        public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(ImageEx), new PropertyMetadata(Stretch.Uniform));

        private const string FailedContentHostTemplateName = "PART_FailedContentHost";
        private const string FailedStateName = "Failed";
        private const string ImageStateGroupName = "ImageStates";
        private const string ImageTemplateName = "PART_Image";
        private const string LoadingContentHostTemplateName = "PART_LoadingContentHost";
        private const string LoadingStateName = "Loading";
        private const string NormalStateName = "Normal";
        private const string OpenedStateName = "Opened";
        private static readonly DependencyPropertyKey IsLoadingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsLoading), typeof(bool), typeof(ImageEx), new PropertyMetadata(default(bool)));

        private Image _image;
        private bool _isInViewport;
        private CancellationTokenSource _lastLoadCts;
        private object _lazyLoadingSource;

        static ImageEx()
        {
            IsLoadingProperty = IsLoadingPropertyKey.DependencyProperty;

            DefaultStyleKeyProperty.OverrideMetadata(typeof(ImageEx), new FrameworkPropertyMetadata(typeof(ImageEx)));
        }

        public ImageEx()
        {
            LayoutUpdated += ImageEx_LayoutUpdated;
        }

        public event EventHandler<ImageExFailedEventArgs> ImageFailed;

        public event EventHandler ImageOpened;

        public bool EnableLazyLoading
        {
            get => (bool)GetValue(EnableLazyLoadingProperty);
            set => SetValue(EnableLazyLoadingProperty, value);
        }

        public DataTemplate FailedTemplate
        {
            get => (DataTemplate)GetValue(FailedTemplateProperty);
            set => SetValue(FailedTemplateProperty, value);
        }

        public DataTemplateSelector FailedTemplateSelector
        {
            get => (DataTemplateSelector)GetValue(FailedTemplateSelectorProperty);
            set => SetValue(FailedTemplateSelectorProperty, value);
        }

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            private set => SetValue(IsLoadingPropertyKey, value);
        }

        public DataTemplate LoadingTemplate
        {
            get => (DataTemplate)GetValue(LoadingTemplateProperty);
            set => SetValue(LoadingTemplateProperty, value);
        }

        public DataTemplateSelector LoadingTemplateSelector
        {
            get => (DataTemplateSelector)GetValue(LoadingTemplateSelectorProperty);
            set => SetValue(LoadingTemplateSelectorProperty, value);
        }

        public int RetryCount
        {
            get => (int)GetValue(RetryCountProperty);
            set => SetValue(RetryCountProperty, value);
        }

        public TimeSpan RetryDelay
        {
            get => (TimeSpan)GetValue(RetryDelayProperty);
            set => SetValue(RetryDelayProperty, value);
        }

        public object Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public Stretch Stretch
        {
            get => (Stretch)GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        public StretchDirection StretchDirection
        {
            get => (StretchDirection)GetValue(StretchDirectionProperty);
            set => SetValue(StretchDirectionProperty, value);
        }

        public override async void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _image = (Image)GetTemplateChild(ImageTemplateName);
            if (Source == null || !EnableLazyLoading || _isInViewport)
            {
                _lazyLoadingSource = null;
                await SetSourceAsync(Source);
            }
            else
            {
                _lazyLoadingSource = Source;
            }
        }

        private static async void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var obj = (ImageEx)d;
            var value = e.NewValue;

            if (obj.Source == null || !obj.EnableLazyLoading || obj._isInViewport)
            {
                obj._lazyLoadingSource = null;
                await obj.SetSourceAsync(value);
            }
            else
            {
                obj._lazyLoadingSource = value;
            }
        }

        private async void ImageEx_LayoutUpdated(object sender, EventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            FrameworkElement hostElement = this;
            while (true)
            {
                var parent = VisualTreeHelper.GetParent(hostElement) as FrameworkElement;
                if (parent == null)
                {
                    break;
                }

                if (parent is ScrollViewer)
                {
                    hostElement = parent;
                    break;
                }

                hostElement = parent;
            }

            if (!ReferenceEquals(hostElement, this))
            {
                var controlRect = TransformToVisual(hostElement)
                    .TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
                var hostRect = new Rect(0, 0, hostElement.ActualWidth, hostElement.ActualHeight);

                if (controlRect.IntersectsWith(hostRect))
                {
                    _isInViewport = true;

                    if (_lazyLoadingSource != null)
                    {
                        var source = _lazyLoadingSource;
                        _lazyLoadingSource = null;
                        await SetSourceAsync(source);
                    }
                }
                else
                {
                    _isInViewport = false;
                }
            }
        }

        private async Task SetSourceAsync(object source)
        {
            if (_image == null)
            {
                return;
            }

            _lastLoadCts?.Cancel();
            if (source == null)
            {
                _image.Source = null;
                VisualStateManager.GoToState(this, NormalStateName, true);
                return;
            }

            _lastLoadCts = new CancellationTokenSource();
            try
            {
                IsLoading = true;

                VisualStateManager.GoToState(this, LoadingStateName, true);

                var context = new LoadingContext<ImageSource>(source, ActualWidth, ActualHeight);

                var pipeDelegate = ImageExService.GetHandler<ImageSource>();
                var retryDelay = RetryDelay;
                var policy = Policy.Handle<Exception>()
                    .WaitAndRetryAsync(RetryCount, count => retryDelay, (ex, delay) =>
                    {
                        context.Reset();
                    });
                await policy.ExecuteAsync(() => pipeDelegate.Invoke(context, _lastLoadCts.Token));

                if (!_lastLoadCts.IsCancellationRequested)
                {
                    _image.Source = context.Result;
                    VisualStateManager.GoToState(this, OpenedStateName, true);
                    ImageOpened?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                if (!_lastLoadCts.IsCancellationRequested)
                {
                    _image.Source = null;
                    VisualStateManager.GoToState(this, FailedStateName, true);
                    ImageFailed?.Invoke(this, new ImageExFailedEventArgs(source, ex));
                }
            }
            finally
            {
                if (!_lastLoadCts.IsCancellationRequested)
                {
                    IsLoading = false;
                }
            }
        }
    }
}