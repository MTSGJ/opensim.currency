# DTL/NSL Secure Money Server
- by Fumi.Iseki and [NSL](http://www.nsl.tuis.ac.jp)
- [Wiki](https://polaris.star-dust.jp/pukiwiki/?OpenSim/MoneyServer)

## 0. Outline
 This Money Server is modified version of DTL Currency Processing.  
 As for this, some bug fixes and some functionality expansions are done. And this can be operated by OpenSim 0.9.x.   
 But, Web Monitor function (ASP.NET) is removed from original DTL Currency. So, this version is less secure than original 
 version!! Please use this at Your Own Risk!!

## 1. Install
#### 1) Compile Opensim or Prepare compiled binary Opensim
#### 2) When you use mono
```
 # cd opensim
 # git clone https://github.com/MTSGJ/opensim.currency.git
 # cd opensim.currensy
 # ./build.sh
```
#### 3) When you use dotnet
```
 # cd opensim
 # git clone https://github.com/MTSGJ/opensim.currency.git
 # cd opensim.currensy
 # ./build.net.sh
```

## 2. Setting
#### 2-1. Money Server
```
 # cd opensim-0.9.x-source
 # vi bin/MoneyServer.ini 
```
- Plese set hostname, database, username and password of MySQL at [MySql] section.
- If you use Banker Avatar, please set UUID of Banker Avatar to "BankerAvatar" in MoneyServer.ini.  
    Banker Avatar can buy money from system with no cost.  
    When 00000000-0000-0000-0000-000000000000 is specified as UUID, all avatars can get money from system.
- If you want to normally use llGiveMoney() function even when payer doesn't login to OpenSim, you must set "true" to "enableForceTransfer" in MoneyServer.ini.
- If you want to send money to anotger avatar by PHP script, you must set "true" to "enableScriptSendMoney". And please set "MoneyScriptAccessKey" and "MoneyScriptIPaddress", too.
    "MoneyScriptAccessKey" is Secret key of Helper Script. Specify same key in include/config.php or WI(XoopenSim/Modlos)  
    "MoneyScriptIPaddress" is IP address of server that Helper Script execute at. Not specify 127.0.0.1.   
- If you want to change Update Balance Messages (blue dialog), pleaase enable and rewrite "BalanceMessage..." valiables.
- Please see also: http://www.nsl.tuis.ac.jp/xoops/modules/xpwiki/?OpenSim%2FMoneyServer%2FMoneyServer.ini

#### 2-2. Region Server
```
 # cd opensim-0.9.x-source
 # vi bin/OpenSim.ini 
```
```
 [Economy]
   SellEnabled = "true"
   CurrencyServer = "https://(MoneyServer's Name or IP):8008/"  
   UserServer = "http://(UserServer's Name or IP):8002/"
   EconomyModule  = DTLNSLMoneyModule

   ;; Money Unit fee to upload textures, animations etc
   PriceUpload = 10

   ;; Money Unit fee to create groups
   PriceGroupCreate = 100
 ```
 #### Attention) 
  - Module name was changed from DTLMoneyModule to DTLNSLMoneyModule
  - Not use 127.0.0.1 or localhost for UserServer's address and CurrencyServer's address. 
    This address is used for identification of user on Money Server.
  - Please see also: http://www.nsl.tuis.ac.jp/xoops/modules/xpwiki/?OpenSim%2FMoneyServer%2FOpenSim.ini

#### 2-3. Helper Script
- If you do not use XoopenSim or Modlos (Web Interface), you should setup helper scripts by manual.
- Please copy Scripts/* to any Web contents directory, and execute setup_sripts.sh shell script.
    And next, edit include/config.php to rewrite ENV_HELPER_URL, ENV_HELPER_PATH, DB information, and etc.etc.
- Please see also: http://www.nsl.tuis.ac.jp/xoops/modules/xpwiki/?OpenSim%2FMoneyServer%2FHelper%20Script
- ex.)
```
  # mkdir /var/www/currency
  # cp -Rpd Scripts/* /var/www/currency
  # cd /var/www/currency
  # ./setup_sripts.sh
  # chown -R www-data.www-data .
  # vi include/config.php
```
- Please execute viewer with "-helperuri [ENV_HELPER_URL/]" option. 
    Here, ENV_HELPER_URL is helper directory url in include/config.php.
- ex.) -helperuri http://localhost/currency/helper/   need last '/'

### 5. License.
 BSD 3-Clause License

### 6. Attention.
 This is unofficial software. Please do not inquire to OpenSim development team or DTL Currency Processing 
 development team about this software. 

### 7. Exemption from responsibility.
 This software is not guaranteed at all. The author doesn't assume the responsibility for the
 problem that occurs along with use, remodeling, and the re-distribution of this software at all.  
 Please use everything by the self-responsibility.

### 8. Address of thanks.
 This Money Server is modified version of DTL Currency Processing.  
 About this project, Milo did a lot of advice and donation to us. 

 Thank you very much!!

