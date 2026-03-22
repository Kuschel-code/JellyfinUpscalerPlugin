# v1.5.4.0 Expansion — All Improvements

## Status: NEARLY COMPLETE

## Block 1: Extended Config Options ✅
- [x] Add new config properties to PluginConfiguration.cs
- [x] Commit

## Block 2: Processing Queue with Priority ✅
- [x] Create ProcessingQueue.cs with PriorityQueue, JobState, persistence
- [x] Add pause/resume/priority endpoints to controller
- [x] Wire into PluginServiceRegistrator
- [x] Commit

## Block 3: Model Auto-Management ✅
- [x] Add disk monitor + LRU eviction to main.py
- [x] Add /models/cleanup endpoint
- [x] Add /models/disk-usage endpoint

## Block 4: Prometheus Metrics ✅
- [x] Add /metrics endpoint to main.py (prometheus format)
- [x] Track jobs, duration, GPU usage, errors per model
- [x] Wire metrics into /upscale, /upscale-frame, /upscale-video-chunk

## Block 5: Health-Check & Auto-Recovery ✅
- [x] Circuit breaker pattern for overloaded service
- [x] /health/detailed endpoint
- [x] Wire circuit breaker into all upscale endpoints

## Block 6: Webhook Notifications ✅
- [x] SendWebhookAsync() in UpscalerCore
- [x] Wire webhooks into LibraryUpscaleScanTask
- [x] Wire webhooks into ImageUpscaleScanTask
- [x] Wire webhooks into VideoProcessor

## Block 7: Model Fallback Chain ✅
- [x] BuildModelChain in UpscalerCore (image upscaling)
- [x] BuildVideoModelChain in VideoProcessor (video processing)
- [x] DI injection of HttpUpscalerService into VideoProcessor
- [x] Fallback chain tries each model before processing

## Block 8: Docs + i18n Update
- [ ] Update README with all new features (metrics, health, webhooks, fallback chain)
- [ ] Update website i18n (6 languages)
- [ ] Final commit for all remaining changes
