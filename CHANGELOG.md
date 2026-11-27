# Changelog

## [1.3.1](https://github.com/DarkArthur/DAS-AudioCD.das/compare/v1.3.0...v1.3.1) (2026-11-27)


### Bug Fixes

* **ci:** wait only for CI checks before auto-merging, the job no longer waits for itself ([8dda5b6](https://github.com/DarkArthur/DAS-AudioCD.das/commit/8dda5b6962bdbd3df4d098b5a963263b2a43454a))

## [1.3.0](https://github.com/DarkArthur/DAS-AudioCD.das/compare/v1.2.0...v1.3.0) (2026-11-27)


### Features

* release under GPL-2.0-or-later, holder DarkArthur DAS, update check from the latest public release ([1ef1e66](https://github.com/DarkArthur/DAS-AudioCD.das/commit/1ef1e66837d6c1bb065b76fd95b7427d8ca94d6d))

## [1.2.0](https://github.com/DarkArthur/DAS-AudioCD.das/compare/v1.1.0...v1.2.0) (2026-11-27)


### Features

* **app:** notify when a newer version is published (version.json), configurable source, disabled by default ([fbd73f1](https://github.com/DarkArthur/DAS-AudioCD.das/commit/fbd73f118fb455f4585095984023f7219d0ad736))

## [1.1.0](https://github.com/DarkArthur/DAS-AudioCD.das/compare/v1.0.1...v1.1.0) (2026-11-27)


### Features

* **installer:** guided Windows setup (Inno Setup) attached to every release ([ebe6c1f](https://github.com/DarkArthur/DAS-AudioCD.das/commit/ebe6c1f8e29d2e57162053775b1159ec393bc216))

## [1.0.1](https://github.com/DarkArthur/DAS-AudioCD.das/compare/v1.0.0...v1.0.1) (2026-11-27)


### Bug Fixes

* **design:** card padding token is a Thickness, app failed to start with a Double cast error ([96cfba5](https://github.com/DarkArthur/DAS-AudioCD.das/commit/96cfba55ca1603ecc5286a1974287e6b1827aae5))

## 1.0.0 (2026-11-27)


### Features

* implement DiscCheck M0-M8 (scanners, pipeline, verify, history, compare, export, degradation, DAS.Design.Charts) ([61bd26b](https://github.com/DarkArthur/DAS-AudioCD.das/commit/61bd26b80df192b4569856ed61bd54ad09f111bb))


### Bug Fixes

* **ci:** cache NuGet by project files, the repo has no packages.lock.json ([abf9f15](https://github.com/DarkArthur/DAS-AudioCD.das/commit/abf9f1519da13bf85d2836754b5daaf58657074e))
* **ci:** keep DAS.Design asset bytes unchanged on Windows checkout ([9167507](https://github.com/DarkArthur/DAS-AudioCD.das/commit/9167507d211dbe951d5c7776f0b70a2742967cd6))
