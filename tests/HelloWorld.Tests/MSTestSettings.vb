Imports Microsoft.VisualStudio.TestTools.UnitTesting

' Class level, not method level. Some tests set process environment variables to exercise
' credential precedence, and parallel methods inside a class would see each other's changes.
<Assembly: Parallelize(Scope:=ExecutionScope.ClassLevel)>
