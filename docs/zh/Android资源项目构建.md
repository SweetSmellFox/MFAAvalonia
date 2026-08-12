# Android 资源项目 APK 构建指南

<!-- markdownlint-disable MD013 -->

本文面向使用 MaaFramework 和 MFAAvalonia 的资源开发者，也可以直接交给 AI 作为实施说明。目标是在不修改资源项目桌面发布逻辑的前提下，为资源仓库生成包含以下内容的 Android APK：

- MFAAvalonia Android UI；
- 资源项目自己的 `interface.json` 或 `interface.jsonc`；
- Pipeline、图片、模型、任务配置等资源；
- 可选的 Python Agent 源码和 python-for-android 运行时；
- 与设备 ABI 对应的 MaaFramework Android Native 库。

本文以 [MaaPracticeBoilerplate](https://github.com/MaaXYZ/MaaPracticeBoilerplate) 为目录示例，并以 MFAAvalonia 仓库中的 [资源项目工作流模板](../../workflows/install.yml) 为准。本文检查模板项目时对应的提交为 [`cf883c0f`](https://github.com/MaaXYZ/MaaPracticeBoilerplate/tree/cf883c0f985a8cc40835481d9fa213dc6d903866)。模板后续发生变化时，应先重新核对它的目录和 `install.yml`，不要机械套用路径。

> [!IMPORTANT]
> MFA 官方发布的 Android APK 是不包含任何项目 interface、资源和 Python runtime 的“空壳示例”。资源开发者应在自己的资源仓库中运行本文所述工作流，生成项目专用 APK。Android Launcher 中显示的应用名称也是构建期信息，不能在安装后根据 `interface.json` 动态修改。

## 1. 先理解三个组成部分

一个资源专用 APK 由三层内容组成：

| 层 | 来源 | 作用 |
| --- | --- | --- |
| Android 空壳 | MFAAvalonia 源码 | 提供 Android UI、Shizuku Native Controller、资源提取和任务运行逻辑 |
| 资源载荷 | 资源仓库 | 提供 interface、Pipeline、图片、模型、任务文件和 Agent 源码 |
| Python runtime，可选 | python-for-android 构建的 AAR | 在 Android 进程中运行 Python Agent；它不是桌面 Python，也不是 Termux |

构建时，资源载荷会被压缩为 `assets/MfaPackage/package.zip` 放入 APK。应用首次启动或载荷指纹变化后，会将它提取到 MFA 数据目录。Python runtime 则作为 AAR 合入 APK，其 Service 负责运行资源载荷里的 Python 入口。

这意味着：

- 只把 `interface.json` 放进 APK，不会自动得到 Pipeline、模型或 Agent；
- 只把 Agent `.py` 文件放进 APK，不代表 APK 内已有 Python 解释器；
- 把 Windows 的 `python.exe`、`.dll`、`.pyd` 打进 APK 不能让它们在 Android 运行；
- `interface.json` 负责声明 Agent，工作流负责提供 Android Python runtime，两者缺一不可；
- UI 版本、资源版本和 APK 应一起发布，Android 端只适合更新资源，不应依赖 UI 自更新来修复版本不匹配。

## 2. 当前验证范围

截至本文编写时，以下链路已经通过本地等价构建和 x86_64 Android 模拟器验证：

- interface、resource、agent 等内容嵌入和启动时提取；
- x86_64 python-for-android AAR 合入 APK；
- Python Agent Service 启动并连接 Maa Agent Client；
- Python Custom Action 注册和运行；
- `print("*info: ...", flush=True)` 输出进入 MFA 首页日志；
- APK 中 MaaFramework、CPython 和资源载荷的构建后检查。

上述 Custom Action 冒烟测试使用的是直接通过 `ctypes` 调用 `libMaaAgentServer.so` 的最小 Agent。第 5.1 节所述官方高层 `maa` Python binding 打包方式是针对 MaaPracticeBoilerplate 的接入方案，仍需在资源项目自己的 GitHub Actions 和目标设备上单独验证，不能用该冒烟结果代替。

以下项目不能仅凭上述本地测试认定已经通过：

- `ubuntu-latest` GitHub Hosted Runner 上完整 YAML 的端到端运行；
- ARM64 真机运行；
- GitHub artifact 上传、Tag Release 和 Release 附件整理；
- 任意第三方 Python 包在 python-for-android 下的兼容性。

第一次接入时应先用 `workflow_dispatch` 构建 CI artifact，再在目标设备验证；验证通过后再打正式 Tag。

## 3. 前置条件

资源仓库需要满足以下条件：

1. 使用 GitHub 托管，并启用 GitHub Actions。
2. 资源包最终根目录内有且仅维护一个 `interface.json` 或 `interface.jsonc`。
3. `interface` 引用的资源、Pipeline、文档和 Agent 源码均已提交，或能在工作流中生成。
4. 使用 Python Agent 时，所有 Python 依赖都有 Android 可用实现或 python-for-android recipe。
5. 最终用户自行安装并启动官方 Shizuku，然后向 MFA 授权。MFA 不内置 Shizuku，也不应携带 Shizuku DLL。

GitHub 公共仓库默认不需要个人访问令牌。工作流可使用 GitHub 自动提供的 `GITHUB_TOKEN`。不要把 PAT、签名密码或其他密钥写入 YAML、代码、构建日志和 artifact；私有依赖确实需要凭据时，使用 GitHub Actions Secrets，并限制权限。

## 4. Android 资源包目录约定

### 4.1 工作流原生支持的统一目录

[模板工作流](../../workflows/install.yml) 默认把 `MFA_ANDROID_RESOURCE_ROOT` 指向的目录视为资源包根目录。推荐的统一目录如下：

```text
项目仓库/
├─ .github/
│  └─ workflows/
│     └─ install.yml
├─ interface.json                 # 或 interface.jsonc，必需
├─ resource/                      # 或 Resource/
│  ├─ pipeline/
│  ├─ image/
│  └─ model/
├─ agent/                         # Python Agent 源码，可选
├─ python/                        # 额外的跨平台 Python 模块，可选
├─ data/                          # Agent/任务数据，可选
├─ tasks/                         # 项目自己的任务配置，可选
├─ android/
│  └─ icon.png                    # Android Launcher 图标，可选，不进入运行时 payload
├─ requirements-android.txt       # 仅作为项目说明；工作流不会自动读取
├─ README.md
└─ LICENSE
```

模板会复制以下目录：

```text
agent  data  python  resource  Resource  tasks
```

模板会复制以下根文件或匹配项：

```text
interface.json
interface.jsonc
changes.json
CONTACT*
LICENSE*
README*
requirements*.txt
*.md
.python-version
pyproject.toml
uv.lock
maa-project.json
```

没有列入以上清单的目录不会自动进入 APK。项目新增运行时目录后，应同时修改工作流中的 `Prepare M9A-style resource payload` 和构建后的验证逻辑。

### 4.2 MaaPracticeBoilerplate 的实际目录

MaaPracticeBoilerplate 当前的核心目录是：

```text
MaaPracticeBoilerplate/
├─ .github/
│  └─ workflows/
│     └─ install.yml
├─ agent/
│  ├─ main.py
│  ├─ my_action.py
│  └─ my_reco.py
├─ assets/
│  ├─ MaaCommonAssets/            # Git submodule
│  ├─ interface.json
│  └─ resource/
│     ├─ image/
│     ├─ model/
│     └─ pipeline/
│        └─ my_task.json
├─ deps/
├─ docs/
├─ tools/
│  ├─ configure.py                # 从 MaaCommonAssets 准备 OCR 模型
│  ├─ install.py                  # 桌面资源安装入口
│  └─ requirements.txt
├─ .gitmodules
├─ LICENSE
└─ README.md
```

这里有两个关键差异：

1. `interface.json` 和 `resource/` 在 `assets/`，但 `agent/` 在仓库根目录。
2. 桌面安装入口是 `tools/install.py <version> <os> <arch>`，不是工作流模板假设的根目录 `install.py <version>`。

因此不能直接把 `MFA_ANDROID_RESOURCE_ROOT` 设为 `assets` 后结束修改：这样能复制 interface 和 resource，却会漏掉同级的 `agent/`。也不建议直接用 MFA 模板全文覆盖 Boilerplate 原有工作流，因为那会破坏其桌面打包步骤。

推荐做法是：保留 Boilerplate 原有桌面 job，只把 MFA 模板中的 `build-android` job 合并进去，并为 Boilerplate 单独组装一个临时 payload。第 7 节给出具体修改方法。

## 5. `interface.json` 中声明 Python Agent

Boilerplate 的 Agent 示例默认是注释状态。启用后可写为：

```jsonc
{
    "interface_version": 2,
    "name": "MaaXXX",
    "agent": {
        "child_exec": "python",
        "child_args": [
            "./agent/main.py"
        ]
    }
}
```

同一份配置可以兼容桌面和 Android：

- 桌面端把 `child_exec` 当作真正的进程命令，并把 Maa Agent socket ID 追加到参数末尾；
- Android 端不启动桌面 `python` 可执行文件，而是让 python-for-android Service 读取 `.py` 入口，再把 socket ID 追加到 `sys.argv` 末尾；
- `MFA_ANDROID_PYTHON_ENTRYPOINT` 非空时优先使用该入口；为空时，Android adapter 会从 `child_exec` 和 `child_args` 中寻找最后一个 `.py` 或 `.pyc` 路径。

为了减少推断差异，建议给 Android 仓库变量显式设置：

```text
MFA_ANDROID_PYTHON_ENTRYPOINT=agent/main.py
```

入口路径相对于提取后的资源包根目录，不是相对于 `.github/workflows`，也不是相对于 `assets/` 原始目录。

### 5.1 让 Boilerplate Agent 在 Android 加载 MaaFramework Python binding

Boilerplate 的 `agent/main.py` 使用：

```python
from maa.agent.agent_server import AgentServer
from maa.toolkit import Toolkit
```

PyPI 上的 `MaaFw` 发布包带有桌面平台依赖，不能假设 python-for-android 能直接安装它。当前建议的 Android 接入方案是：

1. 在工作流中检出与 MFA 使用版本相匹配的 MaaFramework Python binding 源码；
2. 把纯 Python 的 `maa/` 目录复制到 payload 的 `python/maa/`；
3. 通过 P4A 安装 binding 所需、且有 Android recipe 的依赖，例如 `numpy` 和 `strenum`；
4. 使用 APK 已携带的 `libMaaAgentServer.so`，不要再塞桌面 MaaAgentBinary。

当前 MFA 项目中的 `Maa.Framework` binding 版本是 `5.10.0`，对应示例 Tag 为 `v5.10.0`。升级 MFA 后应重新检查 [MFAAvalonia.csproj](../../MFAAvalonia/MFAAvalonia.csproj) 中的版本并同步调整，避免 Python binding 与 Native ABI 长期错配。

在 `agent/main.py` 的所有 `maa` import 之前加入 Android Native 路径兼容：

```python
import os
import sys

android_native_dir = os.environ.get("MAA_LIBRARY_DIR")
if android_native_dir:
    os.environ.setdefault("MAAFW_BINARY_PATH", android_native_dir)

from maa.agent.agent_server import AgentServer
from maa.toolkit import Toolkit

import my_action
import my_reco


def main():
    Toolkit.init_option("./")

    if len(sys.argv) < 2:
        raise RuntimeError("Maa Agent socket ID was not provided")

    print("*info: Android Python Agent server started", flush=True)
    socket_id = sys.argv[-1]
    AgentServer.start_up(socket_id)
    AgentServer.join()
    AgentServer.shut_down()


if __name__ == "__main__":
    main()
```

Android 侧会设置 `MAA_LIBRARY_DIR`、`MAA_FRAMEWORK_LIB_DIR`、`MFA_INSTANCE_ID` 和 `MFA_INSTANCE_NAME`。不要在 Agent 中硬编码应用私有目录或 Native `.so` 的绝对路径。

### 5.2 向首页日志输出信息

Android Python Service 会把完整的 stdout/stderr 行转发给 MFA。以下前缀会进入用户可见日志：

```python
print("info: Agent 已启动", flush=True)
print("success: Custom Action 已完成", flush=True)
print("warn: 参数将使用默认值", flush=True)
print("error: Custom Action 执行失败", flush=True)
```

兼容已有 Agent 的星号形式：

```python
print("*info: Android Python Agent server started", flush=True)
```

支持的等级前缀包括 `trace:`、`debug:`、`success:`、`info:`、`warn:`、`warning:`、`err:`、`error:` 和 `critical:`。没有这些前缀的输出仍会写入 MFA 文件日志，但不保证作为用户消息显示。长时间运行的 Agent 应使用 `flush=True`，否则输出可能滞留在 Python 缓冲区。

## 6. GitHub Actions 仓库变量

在资源仓库进入：

```text
Settings → Secrets and variables → Actions → Variables → New repository variable
```

可用变量如下：

| 变量 | 默认值 | 含义 |
| --- | --- | --- |
| `MFA_ANDROID_RESOURCE_ROOT` | `.` | 统一目录项目的资源包根，相对于资源仓库根目录 |
| `MFA_ANDROID_LAUNCHER_LABEL` | GitHub 仓库名 | Android Launcher 图标下显示的名称 |
| `MFA_ANDROID_LAUNCHER_ICON` | 空（使用 MFA 默认图标） | Android Launcher PNG 图标，相对于资源仓库根目录 |
| `MFA_ANDROID_APPLICATION_ID` | 根据 `owner/repository` 生成 | 资源项目独立且稳定的 Android 包名，例如 `io.github.example.maaxxx` |
| `MFA_ANDROID_PYTHON_REQUIREMENTS` | `python3` | 传给 P4A 的逗号分隔 requirements，不会自动读取 `requirements.txt` |
| `MFA_ANDROID_PYTHON_ENTRYPOINT` | 空 | 强制指定 payload 内的 Python Agent 入口；为空时从 interface 推断 |
| `MFA_ANDROID_P4A_LOCAL_RECIPES` | 空 | P4A local recipes 目录，相对于资源仓库根目录 |
| `MFA_ANDROID_PYTHON_FOR_ANDROID_REF` | `v2026.05.09` | python-for-android Git Tag 或分支 |
| `MFA_ANDROID_MFA_REPOSITORY` | `MaaXYZ/MFAAvalonia` | 要检出的 MFA Android 空壳源码仓库 |
| `MFA_ANDROID_MFA_REF` | `main` | MFA 源码分支、Tag 或完整 commit SHA |

使用 Boilerplate Python Agent 的一个起始配置是：

```text
MFA_ANDROID_LAUNCHER_LABEL=MaaXXX
MFA_ANDROID_LAUNCHER_ICON=android/icon.png
MFA_ANDROID_APPLICATION_ID=io.github.example.maaxxx
MFA_ANDROID_PYTHON_REQUIREMENTS=python3,numpy,strenum
MFA_ANDROID_PYTHON_ENTRYPOINT=agent/main.py
```

如果工作流还没有合入 MFA 官方 `main`，或者需要测试尚未发布的修复，还要设置：

```text
MFA_ANDROID_MFA_REPOSITORY=<包含修复的公开仓库>
MFA_ANDROID_MFA_REF=<包含修复的分支或 commit SHA>
```

本地工作区里的未提交修改不会被 GitHub Runner 看见。`MFA_ANDROID_MFA_REF` 最好在验证后固定到 Tag 或 commit SHA；长期跟随 `main` 虽然方便，但会降低构建可复现性。

Launcher 图标必须是资源仓库内的 PNG 文件。Android 资源编译器不能直接使用桌面项目常见的 `logo.ico`；这类项目可以保留原有 ICO，同时另行导出一份带透明背景的方形 PNG，例如 `android/icon.png`，再通过 `MFA_ANDROID_LAUNCHER_ICON` 指定。该文件只参与 APK 构建，不会复制进 `assets/MfaPackage/package.zip`。修改仓库变量后，可从 Actions 页面手动运行一次 `install`；推荐把图标放在 `android/` 下，这样修改图标文件也会触发模板的路径过滤器。

`MFA_ANDROID_APPLICATION_ID` 决定 Android 眼中的应用身份。未设置时，工作流会把 GitHub 仓库 `Owner/Repo-Name` 规范化为类似 `io.github.owner.repo_name` 的独立包名，不再让所有资源项目共用 `com.fox.MFAAvalonia`。若中文、特殊字符或重名项目规范化后发生冲突，由资源开发者显式设置 `MFA_ANDROID_APPLICATION_ID` 解决。准备公开发布或接入覆盖更新时，强烈建议显式设置并永久保持不变；修改包名会被 Android 当作另一个应用，旧应用的数据和安装关系不会自动迁移。

`MFA_ANDROID_P4A_LOCAL_RECIPES` 指向资源仓库内的目录，例如：

```text
p4a-recipes/
└─ some_native_package/
   ├─ __init__.py
   └─ patches/
```

对应变量为：

```text
MFA_ANDROID_P4A_LOCAL_RECIPES=p4a-recipes
```

路径会进行越界检查，不能使用 `../` 指到资源仓库之外。

## 7. 修改 MaaPracticeBoilerplate 的 `install.yml`

### 7.1 不要直接替换桌面构建

先保留资源项目原来的 `.github/workflows/install.yml`，再从 MFA 的 [workflows/install.yml](../../workflows/install.yml) 复制整个 `build-android` job。Boilerplate 原有 job 名是 `install`，MFA 示例将桌面 job 命名为 `install-desktop`；合并时可以继续保留 Boilerplate 的 `install` 名称。

建议同时从 Boilerplate 原有矩阵中移除伪 Android 项：

```yaml
strategy:
  matrix:
    os: [win, macos, linux]
    arch: [aarch64, x86_64]
```

原来的 `android` 矩阵项只会生成资源/Native 文件目录，不是可安装 APK。真正 APK 由新加入的 `build-android` job 生成。

在工作流顶层加入 Release 所需权限：

```yaml
permissions:
  contents: write
```

为 `push.paths` 和 `pull_request.paths` 至少补充：

```yaml
paths:
  - ".github/workflows/install.yml"
  - "agent/**"
  - "assets/**"
  - "python/**"
  - "data/**"
  - "tasks/**"
  - "requirements*.txt"
  - "**.py"
```

### 7.2 检出 MaaFramework Python binding，仅 Python Agent 需要

在 `build-android.steps` 中，紧跟 `Checkout MFA Android shell` 后加入：

```yaml
- name: Checkout matching MaaFramework Python binding
  uses: actions/checkout@v5
  with:
    repository: MaaXYZ/MaaFramework
    ref: ${{ vars.MFA_ANDROID_MAAFW_REF || 'v5.10.0' }}
    path: maafw-python-source
    sparse-checkout: source/binding/Python/maa
```

这里额外使用了资源项目自己的变量 `MFA_ANDROID_MAAFW_REF`。它不是 MFA 模板内置必填项，只是为了让版本升级时无需改 YAML。它应与当前 MFA 使用的 Python/Native API 版本匹配。

不使用 Python Agent 时，不要加入这个 checkout。

### 7.3 用 Boilerplate 专用 payload 步骤替换通用步骤

将复制过来的 `Prepare M9A-style resource payload` 整步替换为以下内容：

```yaml
- name: Prepare MaaPracticeBoilerplate Android payload
  id: payload
  shell: bash
  run: |
    # Checkout 使用了 submodules: recursive，因此可从 MaaCommonAssets 准备 OCR。
    python3 resource-source/tools/configure.py

    payload_root="$RUNNER_TEMP/mfa-android-resource-payload-${{ matrix.arch }}"
    mkdir -p "$payload_root"

    if [[ ! -f resource-source/assets/interface.json \
       && ! -f resource-source/assets/interface.jsonc ]]; then
      echo "[ERR] assets must contain interface.json or interface.jsonc"
      exit 1
    fi

    for interface_file in interface.json interface.jsonc; do
      if [[ -f "resource-source/assets/$interface_file" ]]; then
        cp -a "resource-source/assets/$interface_file" "$payload_root/$interface_file"
      fi
    done

    if [[ -d resource-source/assets/resource ]]; then
      cp -a resource-source/assets/resource "$payload_root/resource"
    fi

    if [[ -d resource-source/agent ]]; then
      cp -a resource-source/agent "$payload_root/agent"
    fi

    for directory in python data tasks; do
      if [[ -d "resource-source/$directory" ]]; then
        cp -a "resource-source/$directory" "$payload_root/$directory"
      fi
    done

    for root_file in README.md LICENSE; do
      if [[ -f "resource-source/$root_file" ]]; then
        cp -a "resource-source/$root_file" "$payload_root/$root_file"
      fi
    done

    # Python Agent 使用 MaaFw binding 时，打入纯 Python maa 包。
    if [[ -d maafw-python-source/source/binding/Python/maa ]]; then
      mkdir -p "$payload_root/python"
      cp -a maafw-python-source/source/binding/Python/maa \
        "$payload_root/python/maa"
    fi

    echo "root=$payload_root" >> "$GITHUB_OUTPUT"
```

`Checkout resource project` 必须保留：

```yaml
with:
  path: resource-source
  submodules: recursive
```

否则 `assets/MaaCommonAssets` 不会存在，`tools/configure.py` 无法准备 OCR 模型。

这个专用步骤直接生成统一 payload，因此不再依赖 `MFA_ANDROID_RESOURCE_ROOT`。其他目录结构的项目可以继续使用通用步骤并设置该变量。

### 7.4 确保 Android interface 版本与 Tag 一致

Boilerplate 的桌面 `tools/install.py` 会修改安装目录中的 interface 版本，但上面的 Android payload 步骤只是复制源文件。资源作者至少应在发布 Tag 前同步更新 `assets/interface.json` 的 `version`。

如需 CI 自动写入临时 payload，可在 payload 复制完成后、输出 `root` 之前增加项目自己的 JSONC 处理步骤。不要用简单字符串替换去改复杂 JSONC；应使用能读取注释的解析器，并且只修改 `$payload_root` 中的副本，不能回写仓库源码。

### 7.5 修改 Release 依赖和附件整理

Boilerplate 原来的 Release job 应等待 Android job：

```yaml
release:
  if: ${{ needs.meta.outputs.is_release == 'true' }}
  needs: [meta, install, build-android, changelog]
```

原工作流会把每个 artifact 目录都压缩为 ZIP。APK 不应再套一层 ZIP，因此将 Release 文件整理步骤改为：

```yaml
- name: Prepare release files
  shell: bash
  run: |
    cd assets
    for directory in */; do
      directory=${directory%/}
      if compgen -G "$directory/*.apk" > /dev/null; then
        mv "$directory"/*.apk .
      else
        (cd "$directory" && zip -r \
          "../$directory-${{ needs.meta.outputs.tag }}.zip" .)
      fi
      rm -rf "$directory"
    done
```

正式 Tag 触发后，Release 中应同时出现桌面 ZIP 和以下 APK：

```text
<仓库名>-<版本>-android-arm64.apk
<仓库名>-<版本>-android-x64.apk
```

ARM64 APK 面向大多数真机和 ARM64 模拟器；x64 APK 面向 x86_64 模拟器。不能仅根据电脑 CPU 判断模拟器 ABI，可在模拟器设置或 `adb shell getprop ro.product.cpu.abi` 中确认。

## 8. 不使用 Python Agent 时如何精简

没有 Python Agent 的资源项目不需要打入 Python runtime。为了让 YAML 关系明确，建议做完整精简，而不是只删除其中一步：

1. 删除 `Setup build Python`；
2. 删除 `Install python-for-android host dependencies`；
3. 删除 `Build python-for-android service AAR`；
4. 从 publish step 的环境变量中删除 `PYTHON_AAR`；
5. 从 `dotnet publish` 参数中删除 `AndroidPythonRuntimeAar` 和 `AndroidPythonAgentEntryPoint`；
6. 从 APK 验证 step 中删除 `PYTHON_AAR` 及 CPython 检查；
7. 删除 `P4A_REQUIREMENTS`、`P4A_ENTRYPOINT`、`P4A_LOCAL_RECIPES` 和 `P4A_REF` 等不再使用的 job 环境变量；
8. 不检出 MaaFramework Python binding，也不把 `python/maa` 放入 payload；
9. 从 `interface.json` 删除或保持注释状态的 `agent` 配置。

Android Native Controller 本身不依赖 Python Agent，因此仍可运行纯 Pipeline 任务。

## 9. Python requirements 和 recipe

`MFA_ANDROID_PYTHON_REQUIREMENTS` 是直接传给 P4A `--requirements` 的逗号分隔字符串。例如：

```text
python3,numpy,strenum
```

资源仓库中的 `requirements.txt` 只会作为 payload 文件复制，不会自动控制 P4A。这样设计是为了避免把桌面专用依赖误装进 Android。建议把依赖拆开记录：

```text
requirements-desktop.txt
requirements-android.txt
```

然后显式同步 Android 变量。新增依赖前逐项确认：

- 是否是纯 Python 包；
- PyPI 是否提供源码包；
- 是否依赖 C/C++、Rust、Fortran 或桌面动态库；
- python-for-android 是否已有 recipe；
- ARM64 和 x86_64 是否都能构建；
- 包运行时是否假设 Windows、Linux 桌面或系统命令存在。

只有 Windows wheel 或 `.pyd` 的包不能直接使用。可选解决方式是：

- 改用纯 Python/Android 兼容替代包；
- 为 P4A 编写 local recipe；
- 将性能敏感逻辑改为 APK 自带的 Android Native 库并提供明确绑定；
- 将功能在 Android interface 中隐藏或禁用。

不要把桌面便携 Python 目录整体放进 payload 的 `python/`。这里的 `python/` 应只包含需要随资源分发的跨平台模块；真正的 Android CPython 和 site-packages 来自 P4A AAR。

## 10. 构建和下载 APK

### 10.1 首次手动构建

1. 提交工作流、interface、资源和 Agent 修改。
2. 打开资源仓库的 `Actions` 页面。
3. 选择 `install`。
4. 点击 `Run workflow`，选择测试分支。
5. 等待 `build-android` 的 `android-arm64` 和 `android-x64` matrix 完成。
6. 在本次 Run 的 `Artifacts` 下载 APK artifact。

普通分支 push、PR 和 `workflow_dispatch` 只生成 artifact，不创建正式 Release。只有 `v*` Tag 会令 `meta.is_release=true` 并运行 Release job。例如：

```bash
git tag v1.0.0
git push origin v1.0.0
```

### 10.2 工作流内部的自动检查

`Verify and collect Android APK` 会确认：

- 生成了 `*-Signed.apk`；
- APK 内存在 `assets/MfaPackage/package.zip`；
- APK 内存在当前 ABI 的 `libMaaFramework.so`；
- 启用 Python runtime 时，APK 内存在当前 ABI 的 `libpython3.x.so`。

这些检查能发现漏打包，但不能证明任务逻辑正确。至少还应在设备上验证：

1. Launcher 名称正确；
2. 首次启动不闪退；
3. interface 中任务和选项完整；
4. 图片、模型和 Pipeline 能加载；
5. Shizuku 已启动、授权成功并能获取画面；
6. Python Agent 日志出现；
7. Custom Action 或 Custom Recognition 实际被调用；
8. 停止任务后 Python Service 正常退出；
9. 覆盖安装新资源版本后能按新指纹重新提取。

## 11. Android 设备侧使用

1. 安装匹配 ABI 的项目专用 APK。
2. 安装并启动 Shizuku。所有 MFA Android APK 都直接内置固定版本且预先校验过的官方 Shizuku 安装包；若设备上未安装，MFA 会在启动后通过 Android 系统安装器引导安装。用户仍需手动确认并按 Shizuku 指引启动服务。
3. 打开 MFA，接受 Shizuku 授权请求。
4. 检查首页任务列表、任务选项和资源配置。
5. 启动任务。Android 会使用 Shizuku Native Controller，不回退到 ADB。

Boilerplate interface 中的 `Adb` controller 可以为桌面版保留。Android 端的平台控制器由 MFA 初始化，不需要用户填写 ADB 地址。目标包名不是建立 Controller 的前提，只在项目确实使用 `StartApp` 等需要应用标识的能力时才有意义。

Python Agent 不是打开 UI 时立即启动，而是在任务器初始化、发现 interface 含 Agent 配置时启动。因此只打开应用看不到“Agent started”日志是正常现象；需要实际开始一次依赖 Agent 的任务。

当前 Android 只允许一个 python-for-android Agent Service 会话同时运行。MFA 可以保存和切换多套配置，但不能并行多开真正的任务实例。

APK 覆盖安装后，MFA 会根据内嵌资源指纹执行受控替换：先把新 payload 解压到临时目录并确认存在 interface，再备份并清理上一版由 bootstrap 管理的 `resource`、`agent`、`python`、`tasks` 等内容，最后换入新版本。这样上游已经删除的 Pipeline、图片或脚本不会残留；配置、用户日志、导出文件和其他非 payload 数据不会被清理。替换失败会尝试恢复旧 payload。

### 11.1 Shizuku 安装检测的边界

MFA 通过官方包名 `moe.shizuku.privileged.api` 判断 Shizuku Manager 是否安装，并在 Android Manifest 的 `<queries>` 中声明该包名以适配 Android 11 及以上的包可见性限制。安装检测、Binder 是否运行、MFA 是否获得授权是三个不同状态：

- 未安装：启动后显示安装引导，使用 APK 内置的官方安装包；
- 已安装但服务未运行：Controller 初始化会明确提示用户启动 Shizuku；
- 服务已运行但未授权：由 Shizuku 官方授权流程请求用户确认。

启动引导中的“稍后处理”只跳过本次进程的提示，不会永久关闭检测。MFA 源码仓库当前直接保存官方 Shizuku `v13.6.0`，文件 SHA-256 为 `6e273ab0e991c4e79bc8b1bbb9b9dd739cca1a8712a541a214078886b7b790f`，构建后位于 `assets/MfaDependencies/shizuku.apk`。MFA 先将只读 asset 复制到私有缓存，再用 FileProvider 交给 Android 系统安装器；不会静默安装。

升级内置版本时必须从 [RikkaApps/Shizuku 官方 Release](https://github.com/RikkaApps/Shizuku/releases) 获取 APK，独立核对版本、文件名、SHA-256 和许可证，并同步更新 `MFAAvalonia.Android/ThirdParty/Shizuku/README.md`。不要从第三方镜像获取 APK。

### 11.2 应用自更新研究结论

MAA-Meow 的应用更新链路大致为：检查稳定版/测试版版本 → 从 GitHub 或 MirrorChyan 解析对应 APK → 下载到应用缓存并用临时扩展名保证完整性 → 通过 FileProvider 向系统安装器提供 APK → 用户授权“安装未知应用”后覆盖安装。它同时使用稳定的包名、递增的 Android `versionCode`、可比较的 `versionName` 和一致的签名证书。

MFA 的资源工作流构建结果本身就是包含 UI、interface、resource 和可选 Python runtime 的完整 APK，因此自更新确实可以直接从该资源项目自己的 GitHub Release 下载匹配 ABI 的 APK，再交给 Android 系统安装器覆盖当前应用。不能下载 MaaXYZ/MFAAvalonia 发布的空壳 APK，否则会丢失项目资源。正式接入前仍必须先解决：

1. 每个资源项目使用独立且稳定的 Android Application ID，避免不同项目互相覆盖；
2. 每次构建使用同一份受 Secrets 保护的签名密钥，否则 Android 拒绝覆盖安装；
3. Release Tag 映射为递增的整数 `versionCode` 和准确的 `versionName`，不能继续固定为 `1` / `1.0`；
4. 更新元数据明确区分 ARM64、x64，并校验下载 APK 的 SHA-256、包名、版本和签名；
5. 增加 FileProvider、缓存清理、断点/临时文件策略和系统“安装未知应用”授权引导；
6. 更新源、仓库和资产命名由资源项目在构建期注入，官方空壳默认关闭应用自更新；
7. UI 更新与资源更新必须作为同一个资源项目 APK 发布，避免 interface 与 UI 版本错配。

因此当前阶段继续保留已有的资源更新功能，不直接启用 APK 自更新。下一阶段应先扩展 `install.yml` 的 Application ID、版本号和稳定签名输入，再实现只接受同包名、同签名资源 APK 的下载与系统安装链路。

## 12. 常见故障

### 12.1 `Resource root must contain interface.json or interface.jsonc`

原因通常是 `MFA_ANDROID_RESOURCE_ROOT` 指错，或者照搬 Boilerplate 时把根设成 `.`。Boilerplate 的 interface 在 `assets/`，应使用第 7 节专用 payload 步骤，而不是只改一个变量。

### 12.2 APK 能启动，但没有任务或像没加载 interface

检查：

- APK 内是否有 `assets/MfaPackage/package.zip`；
- 内层 ZIP 根目录是否直接包含 interface，而不是多套一层 `assets/`；
- interface 的任务 entry 是否能在已复制的 Pipeline 中找到；
- 是否同时保留了内容不同的 `interface.json` 和 `interface.jsonc`；
- 覆盖安装后资源指纹是否变化，应用是否完成重新提取。

正确内层结构应是：

```text
package.zip
├─ interface.json
├─ resource/
├─ agent/
└─ python/
```

而不是：

```text
package.zip
└─ assets/
   ├─ interface.json
   └─ resource/
```

### 12.3 `No Python script was found`

Android adapter 没能从 `agent.child_exec` 或 `agent.child_args` 找到 `.py/.pyc`。设置：

```text
MFA_ANDROID_PYTHON_ENTRYPOINT=agent/main.py
```

并确认 `package.zip` 中确实有 `agent/main.py`。

### 12.4 `This Android APK does not contain a platform Agent runtime`

APK 没有合入 P4A AAR，却在 interface 中启用了 Python Agent。恢复三个 Python runtime steps 和 publish 参数，或从 interface 移除 Agent。

### 12.5 `ModuleNotFoundError: No module named 'maa'`

P4A AAR 只有解释器和 requirements，没有找到 MaaFramework Python binding。按第 7.2、7.3 节检出匹配版本并把 `maa/` 复制到 `payload/python/maa/`。

不要通过塞入桌面 `MaaAgentBinary` 来掩盖问题。Android APK 已携带匹配 ABI 的 Native MaaFramework 库。

### 12.6 找不到 `libMaaAgentServer.so`

确保 `agent/main.py` 在 import `maa` 前将 Android 提供的 `MAA_LIBRARY_DIR` 映射到 `MAAFW_BINARY_PATH`，并确认 APK 验证或文件列表中存在当前 ABI 的 `libMaaAgentServer.so`。

### 12.7 P4A 构建依赖失败

先定位失败的具体 requirement。常见原因是只有桌面 wheel、没有源码包、缺少 recipe，或 recipe 不支持当前 NDK。不要反复无差别重跑完整 APK；先把 matrix 临时缩减到一个 ABI，解决该依赖的 P4A 构建，再恢复 ARM64 和 x64。

### 12.8 `Shizuku UserService is not ready`

这与 Python Agent 是两条独立链路。确认：

- 用户安装的是官方 Shizuku；
- Shizuku 当前显示正在运行；
- MFA 已获得授权；
- 授权或服务状态变化后重新进入 MFA；
- 安装的是最新测试 APK，而不是旧的 ADB fallback 构建。

MFA 不应静默安装或替代 Shizuku。若资源 APK 选择内置安装引导，只能使用工作流中固定版本并经 SHA-256 校验的官方 Shizuku APK，最终安装操作仍必须交给 Android 系统安装器和用户确认。

### 12.9 Agent 有运行但首页没有输出

使用完整行和受支持的等级前缀，并强制 flush：

```python
print("*info: Agent server started", flush=True)
```

普通无前缀 stdout 主要进入文件日志。还要确认任务已真正进入 Agent 初始化阶段，而不只是打开了应用。

### 12.10 安装失败或启动即闪退

先检查 ABI 是否匹配。x64 APK 通常用于 x86_64 模拟器，ARM64 APK 通常用于真机。然后导出 MFA 日志和 Android `logcat`，区分以下阶段：

- APK 主进程启动即崩溃；
- 资源提取失败；
- Shizuku Controller 初始化失败；
- 点击任务后 Python Service 崩溃；
- Agent 已启动但 Native 连接失败。

不要只根据 UI 的“连接失败”判断 Python、Shizuku 或资源是哪一层出错，应以异常堆栈中的类名和阶段日志为准。

### 12.11 `AndroidEnvironmentInternal.UnhandledException is inaccessible`

若 Release APK 在 AndroidX 回调中崩溃，且日志同时包含 `System.MethodAccessException` 和 `_mm_wrapper`，这是 [.NET for Android 的 marshal methods 已知问题](https://github.com/dotnet/android/issues/10602)，不是 interface、资源、Shizuku、Python Agent 或 `libmfabridge.so` 首先引起的。MFA Android 壳已显式设置：

```xml
<AndroidEnableMarshalMethods>false</AndroidEnableMarshalMethods>
```

资源工作流应检出包含该设置的 MFA 版本。若资源仓库通过 `MFA_ANDROID_MFA_REF` 固定到了旧提交，应更新到修复后的提交再重新构建；不要在资源项目里删除或覆盖这个属性。崩溃栈末尾出现 `libmfabridge.so` 的 `std::terminate` 通常只是托管进程因未处理异常退出时的后续清理栈。禁用 marshal methods 后若仍有异常，应以新日志暴露出的首个原始异常继续排查。

## 13. 给 AI 的实施契约

将本文交给 AI 修改资源仓库时，可以要求它严格遵守以下约束：

1. 先读取资源仓库当前 `.github/workflows/install.yml`、interface、Agent 入口、目录树和 git 状态。
2. 保留现有桌面打包行为；MaaPracticeBoilerplate 不得改成调用根目录 `install.py`。
3. Android payload 根必须直接包含 interface，并完整包含 interface 实际引用的资源。
4. 不得把本地 `bin/Debug`、Windows Python、构建缓存、签名文件、Token 或用户日志提交到仓库。
5. 不使用 Python Agent 时必须完整移除 P4A block 及其输出引用。
6. 使用 Python Agent 时必须同时验证入口、P4A requirements、Maa Python binding 和 Native 库版本。
7. `MFA_ANDROID_MFA_REF` 指向的代码必须已推送；不得假设 GitHub Runner 能读取本地未提交修改。
8. 先运行 YAML/JSONC/路径静态检查，再触发 CI；依赖不确定时先测试单 ABI。
9. 只有 GitHub `build-android` 两个 matrix、设备启动、Shizuku 画面、Agent 连接和至少一个真实任务都通过后，才能报告“Android 发布链路验证完成”。
10. 如果只完成本地等价构建，必须明确写“未完成真实 GitHub Actions 端到端验证”。

建议 AI 最终输出以下验收信息：

```text
- 修改的工作流文件：
- 资源 payload 根：
- interface 实际路径：
- Android Agent 入口：
- P4A requirements：
- MFA repository/ref：
- MaaFramework Python binding ref：
- 生成的 ABI：
- GitHub Actions Run URL：
- artifact 名称：
- 真机/模拟器验证结果：
- 尚未验证的风险：
```

## 14. 发布前检查清单

- [ ] 保留原桌面构建并成功生成各平台 ZIP。
- [ ] 原桌面矩阵不再把普通文件目录伪装成 Android APK。
- [ ] `build-android` 同时构建 `android-arm64` 和 `android-x64`。
- [ ] Launcher 名称使用项目名称。
- [ ] 如需项目专属图标，`MFA_ANDROID_LAUNCHER_ICON` 指向仓库内有效的 PNG。
- [ ] payload 根直接包含 interface。
- [ ] 所有 Pipeline、图片、模型、Agent 和任务数据都被复制。
- [ ] OCR 等由 submodule 生成的资源已在 payload 准备前生成。
- [ ] interface 版本与 Release Tag 一致。
- [ ] Python Agent 入口可推断或已显式设置。
- [ ] P4A requirements 不包含仅桌面可用的 wheel。
- [ ] Maa Python binding 与 Native API 版本匹配。
- [ ] APK 内存在资源包、MaaFramework 和对应 ABI 的 CPython。
- [ ] Shizuku 启动和授权流程已在目标 Android 版本验证。
- [ ] 首页能收到 Agent 的 `*info:` 测试消息。
- [ ] 至少一个 Custom Action/Recognition 和一个正常 Pipeline 任务实际执行成功。
- [ ] Release 附件中的 APK 没有被再次压成 ZIP。
- [ ] 工作流和仓库中没有个人 Token、签名密钥或本地绝对路径。
