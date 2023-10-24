## 사용 방법

![Pasted image 20230524175824](https://github.com/netter36/UnityUtilities/assets/21096117/9ab86b9c-92f3-4282-9fc7-2e27667499ad)

![Pasted image 20230524175844](https://github.com/netter36/UnityUtilities/assets/21096117/cd9efb1c-de2e-4b80-9a4b-3da92691bcc3)

```c#
[MultiShowIf(true, nameof(isValue1), nameof(isValue2))]  
public string testValue1;  
  
[MultiEnableIf(false, nameof(isValue1), nameof(isValue2))]  
public string testValue2;  
  
public bool isValue1;  
public bool isValue2;
```
