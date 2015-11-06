rem $(SolutionDir) = %1, $(ProjectDir) = %2, $(OutDir) = %3

rem Creating the modules directory...
mkdir %2%3modules

rem Copying modules...
mkdir %2%3modules\GoodAI.BasicNodes
xcopy /y /s %2..\..\Modules\BasicNodes\Module\bin\%4\*.* %2%3\modules\GoodAI.BasicNodes

rem mkdir %2%3modules\GoodAI.InternalNodes
rem xcopy /y /s %2..\..\Modules\InternalNodes\Module\bin\%4\*.* %2%3\modules\GoodAI.InternalNodes

mkdir %2%3modules\GoodAI.MNIST
xcopy /y /s %2..\..\Modules\MNIST\Module\bin\%4\*.* %2%3\modules\GoodAI.MNIST

rem mkdir %2%3modules\GoodAI.SeMeIntegration
rem xcopy /y /s %2..\..\Modules\SeMeIntegrationModule\Module\bin\%4\*.* %2%3\modules\GoodAI.SeMeIntegration