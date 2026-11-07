# Tools 一次确认整项目升级验证

当前目标版本：ACC0.5.3、AO0.3.3、ASS0.9.4、Viseme0.2.3。
[结果索引](summary.json)记录五种当前插件消费者：all、acc、ass、ao、viseme。

| 当前消费者 | 所属插件批次 | 实际替换脚本引用 | Inspector 对象 |
| --- | --- | --- | --- |
| all | 4 | 12 | 24 |
| acc | 1 | 6 | 12 |
| ass | 1 | 2 | 4 |
| ao | 1 | 2 | 4 |
| viseme | 1 | 2 | 4 |

每个工程只安装当前插件 ZIP 和其实际依赖。输入来自
[旧包生产者归档](../ComponentReferences/LegacyUpgrade/README.md)中真实 Unity 创建的原始资源、
脚本 GUID、helper metadata、字段与引用身份；没有发明一个固定的旧 GUID。
旧包生产者版本为 e62c741，对应输入 SHA 在各 Scope 的 input-provenance.json 中记录。

原始 Base、Variant、Nested 和 Avatar 资源按原字节与原 metadata 放入四个不同的 Assets
目录。资源 GUID、组件 fileID、引用内容不因路径变化而改写；旧包创建时的属性和
GlobalObjectID 作为逐项比较依据。当前选择指向不相关的 SelectionSentinel.txt。

验证通过真实菜单的 `ComponentReferenceUpgrade.Execute(Func<bool>)` 路径执行：

- 先取消一次，检查确认回调只调用一次，所有输入字节保持。
- 再确认一次，自动扫描整个 Assets；不提供选择范围或 GUID 类型映射。
- 每包只修复自身类型；ACC Part Group 由 Outfit/Prefab 来源自动识别。
- 每批备份与该批立即写入前的原件一致；后续插件保留前批修复与其它插件旧 GUID。
- 含新旧同类组件的重复场景也参与整个 Assets 扫描，其原文件和两份组件保持。
- 比较全部原组件字段、资源 GUID、fileID、父对象引用、Variant/嵌套/实例覆盖和 SDK Descriptor。
- 创建当前 Inspector 并等待延迟回调后再次比较，确认源配置保持。

各 Scope 的 migrated.json、run.json、源码/metadata/DLL SHA、post gzip、各包备份和
报告保存实际执行。sha256.json 校验所有归档；Editor/launcher 日志保留在工作区。
资源输入的原始 gzip 与旧 DLL 来源保留在链接的生产者归档和私有测试工程。

全库功能、图形、场景状态/选择恢复、未保存编辑保护及 ACC 完整 Avatar 工作流的原生检查另见
[当前验证索引](../../VerificationResults.json)。这些 NUnit 检查与真实旧资源重放按各自范围计数。
