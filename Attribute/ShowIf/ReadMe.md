![Pasted image 20230524175441](https://github.com/netter36/UnityUtilities/assets/21096117/d45d7b0c-8903-4353-b308-f429f88bad58)

![Pasted image 20230524175020](https://github.com/netter36/UnityUtilities/assets/21096117/daf6e302-3904-4fca-a05a-3b15b58fbbda)

```c#
[ShowIf(nameof(isValue), true)]  
public string testValue1;  
  
[ShowIf(nameof(number), 5)]  
public string testValue2;  
  
[EnableIf(nameof(number), 3)]
public string testValue3;  
  
public bool isValue;
public int number;
```
