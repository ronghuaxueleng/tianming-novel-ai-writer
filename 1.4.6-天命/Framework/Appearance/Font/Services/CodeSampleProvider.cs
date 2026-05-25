using System;
using System.Collections.Generic;
using System.Linq;

namespace TM.Framework.Appearance.Font.Services
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum CodeLanguage
    {
        CSharp,
        Python,
        JavaScript,
        TypeScript,
        Java,
        Cpp,
        Go,
        Rust,
        JSON,
        XML,
        HTML,
        CSS,
        SQL,
        Markdown
    }

    public class CodeSample
    {
        public CodeLanguage Language { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class CodeSampleProvider
    {
        private readonly Dictionary<CodeLanguage, CodeSample> _samples;

        public CodeSampleProvider()
        {
            _samples = InitializeSamples();
        }

        public List<CodeLanguage> GetSupportedLanguages()
        {
            return _samples.Keys.ToList();
        }

        public CodeSample GetSample(CodeLanguage language)
        {
            return _samples.TryGetValue(language, out var sample) ? sample : _samples[CodeLanguage.CSharp];
        }

        private Dictionary<CodeLanguage, CodeSample> InitializeSamples()
        {
            return new Dictionary<CodeLanguage, CodeSample>
            {
                [CodeLanguage.CSharp] = new CodeSample
                {
                    Language = CodeLanguage.CSharp,
                    DisplayName = "C#",
                    Description = "C# 现代语法特性演示",
                    Code = @"// C# 现代语法特性演示
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Example
{
    public class DataProcessor
    {
        // 属性初始化器
        public string Name { get; init; } = ""Processor"";
        public int MaxRetries { get; set; } = 3;

        // 记录类型（C# 9.0+）
        public record Person(string Name, int Age);

        // 异步方法与LINQ
        public async Task<List<int>> ProcessDataAsync(IEnumerable<int> data)
        {
            // 模式匹配
            var result = data switch
            {
                null => throw new ArgumentNullException(nameof(data)),
                [] => new List<int>(),
                _ => data.Where(x => x > 0)
                         .Select(x => x * 2)
                         .ToList()
            };

            // 空合并赋值（??=）
            result ??= new List<int>();

            // Lambda 表达式
            await Task.Run(() => result.ForEach(x => 
                Console.WriteLine($""Processed: {x}"")));

            return result;
        }

        // 本地函数
        private bool IsValid(int value)
        {
            return value >= 0 && value <= 100;

            // 连字符号测试
            // != == >= <= => -> ?? ??= ||= &&
        }
    }
}"
                },
                [CodeLanguage.Python] = new CodeSample
                {
                    Language = CodeLanguage.Python,
                    DisplayName = "Python",
                    Description = "Python 3.x 特性演示",
                    Code = @"# Python 3.x 特性演示
import asyncio
from typing import List, Dict, Optional
from dataclasses import dataclass

@dataclass
class Person:
    """"""数据类示例""""""
    name: str
    age: int
    email: Optional[str] = None

    def greet(self) -> str:
        return f""Hello, I'm {self.name}!""

# 装饰器
def timer(func):
    """"""计时装饰器""""""
    import time
    def wrapper(*args, **kwargs):
        start = time.time()
        result = func(*args, **kwargs)
        print(f""{func.__name__} took {time.time() - start:.2f}s"")
        return result
    return wrapper

# 列表推导式
@timer
def process_data(numbers: List[int]) -> List[int]:
    # 列表推导
    squared = [x ** 2 for x in numbers if x % 2 == 0]

    # 字典推导
    mapping = {x: x ** 2 for x in numbers}

    # 生成器表达式
    result = (x for x in squared if x > 10)

    return list(result)

# 异步函数
async def fetch_data(url: str) -> Dict:
    await asyncio.sleep(1)  # 模拟网络请求
    return {""url"": url, ""status"": ""ok""}

# 海象运算符 := (Python 3.8+)
def check_length(text: str) -> bool:
    if (n := len(text)) > 10:
        print(f""Length: {n}"")
        return True
    return False

# 连字符号测试: != == >= <= -> => **"
                },
                [CodeLanguage.JavaScript] = new CodeSample
                {
                    Language = CodeLanguage.JavaScript,
                    DisplayName = "JavaScript",
                    Description = "JavaScript ES6+ 特性演示",
                    Code = @"// JavaScript ES6+ 特性演示

// 箭头函数与解构
const processData = ({ name, age, ...rest }) => {
    console.log(`Name: ${name}, Age: ${age}`);
    return { name, age, extra: rest };
};

// 类定义
class DataProcessor {
    #privateField = 0; // 私有字段

    constructor(name) {
        this.name = name;
        this.items = [];
    }

    // 异步方法
    async fetchData(url) {
        try {
            const response = await fetch(url);
            const data = await response.json();
            return data;
        } catch (error) {
            console.error('Error:', error);
            throw error;
        }
    }

    // 链式调用
    addItem(item) {
        this.items.push(item);
        return this; // 支持链式调用
    }

    // 高阶函数
    transform(callback) {
        return this.items.map(callback);
    }
}

// Promise链
const loadData = () => {
    return fetch('/api/data')
        .then(response => response.json())
        .then(data => data.filter(item => item.active))
        .catch(error => console.error(error));
};

// 可选链 ?.
const user = { profile: { name: 'John' } };
const userName = user?.profile?.name ?? 'Anonymous';

// 数组方法
const numbers = [1, 2, 3, 4, 5];
const result = numbers
    .filter(x => x % 2 === 0)
    .map(x => x ** 2)
    .reduce((sum, x) => sum + x, 0);

// 连字符号测试: => != === !== >= <= -> ?? ||= &&="
                },
                [CodeLanguage.TypeScript] = new CodeSample
                {
                    Language = CodeLanguage.TypeScript,
                    DisplayName = "TypeScript",
                    Description = "TypeScript 类型系统演示",
                    Code = @"// TypeScript 类型系统演示

// 接口定义
interface User {
    id: number;
    name: string;
    email?: string; // 可选属性
    readonly createdAt: Date; // 只读属性
}

// 泛型类
class Repository<T extends { id: number }> {
    private items: Map<number, T> = new Map();

    add(item: T): void {
        this.items.set(item.id, item);
    }

    get(id: number): T | undefined {
        return this.items.get(id);
    }

    findAll(predicate: (item: T) => boolean): T[] {
        return Array.from(this.items.values())
            .filter(predicate);
    }
}

// 联合类型与类型守卫
type Result<T> = 
    | { success: true; data: T }
    | { success: false; error: string };

function processResult<T>(result: Result<T>): T {
    if (result.success) {
        return result.data;
    } else {
        throw new Error(result.error);
    }
}

// 工具类型
type ReadonlyUser = Readonly<User>;
type PartialUser = Partial<User>;
type UserKeys = keyof User;

// 装饰器
function Log(target: any, key: string, descriptor: PropertyDescriptor) {
    const originalMethod = descriptor.value;
    descriptor.value = function(...args: any[]) {
        console.log(`Calling ${key} with`, args);
        return originalMethod.apply(this, args);
    };
}

class Service {
    @Log
    async fetchData(url: string): Promise<any> {
        const response = await fetch(url);
        return response.json();
    }
}

// 连字符号测试: => != === >= <= -> ?? ||= &&="
                },
                [CodeLanguage.JSON] = new CodeSample
                {
                    Language = CodeLanguage.JSON,
                    DisplayName = "JSON",
                    Description = "JSON 数据结构示例",
                    Code = @"{
  ""name"": ""字体配置"",
  ""version"": ""1.0.0"",
  ""author"": {
    ""name"": ""开发者"",
    ""email"": ""dev@example.com""
  },
  ""settings"": {
    ""theme"": ""dark"",
    ""fontSize"": 14,
    ""fontFamily"": ""Consolas"",
    ""enableLigatures"": true,
    ""lineHeight"": 1.6
  },
  ""languages"": [
    {
      ""id"": ""csharp"",
      ""extensions"": ["".cs"", "".csx""],
      ""syntax"": ""C-style""
    },
    {
      ""id"": ""python"",
      ""extensions"": ["".py"", "".pyw""],
      ""syntax"": ""indent-based""
    },
    {
      ""id"": ""javascript"",
      ""extensions"": ["".js"", "".jsx""],
      ""syntax"": ""C-style""
    }
  ],
  ""features"": [
    ""语法高亮"",
    ""代码折叠"",
    ""智能提示"",
    ""连字支持""
  ],
  ""metadata"": {
    ""created"": ""2024-01-01T00:00:00Z"",
    ""modified"": ""2024-01-15T12:30:00Z"",
    ""tags"": [""config"", ""editor"", ""font""]
  }
}"
                },
                [CodeLanguage.Markdown] = new CodeSample
                {
                    Language = CodeLanguage.Markdown,
                    DisplayName = "Markdown",
                    Description = "Markdown 语法演示",
                    Code = @"# Markdown 语法演示

## 二级标题

### 文本格式

这是**粗体**文本，这是*斜体*文本，这是~~删除线~~文本。

### 列表

#### 无序列表
- 项目 1
- 项目 2
  - 子项目 2.1
  - 子项目 2.2
- 项目 3

#### 有序列表
1. 第一项
2. 第二项
3. 第三项

### 代码

内联代码：`const x = 42;`

代码块：
```javascript
function greet(name) {
    return `Hello, ${name}!`;
}
```

### 链接与图片

[链接文本](https://example.com)
![图片描述](image.png)

### 引用

> 这是一个引用块
> 可以包含多行

### 表格

| 列1 | 列2 | 列3 |
|-----|-----|-----|
| A   | B   | C   |
| 1   | 2   | 3   |

### 分隔线

---

### 任务列表

- [x] 已完成任务
- [ ] 未完成任务
- [ ] 待办事项"
                }
            };
        }
    }
}

