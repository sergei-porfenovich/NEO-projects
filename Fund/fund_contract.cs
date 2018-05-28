using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework.Services.System;

namespace NeoContract7
{
    public class Lock : SmartContract
    {
        [DisplayName("transferAsset")] public static event Action<byte[], byte[], byte[], BigInteger> TransferAsset;

        [DisplayName("transfer")] public static event Action<byte[], byte[], BigInteger> Transferred;

        // It's an Asset id of Neo
        private static readonly byte[] NeoAssetId =
        {
            155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92,
            34, 12, 205, 207, 110, 252, 51, 111, 197
        };

        // It's an Asset id of Gas
        private static readonly byte[] GasAssetId =
        {
            231, 45, 40, 105, 121, 238, 108, 177, 183, 230, 93, 253, 223, 178, 227, 132, 16, 11, 141, 20, 142, 119, 88,
            222, 66, 228, 22, 139, 113, 121, 44, 96
        };


        public static bool Main(uint timestamp, uint minimumValue)
        {
            Init(timestamp, minimumValue);
            Header header = Blockchain.GetHeader(Blockchain.GetHeight());
            // Neo or gas is being deposited via invocation
            if (DepositAsset() == true)
            {
                if (header.Timestamp < timestamp)
                {
                    return false;
                }

                Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
                TransactionOutput senderReference = tx.GetReferences()[0];
                TransactionOutput[] reference = tx.GetReferences();
                byte[] from = senderReference.ScriptHash;
                foreach (TransactionOutput output in reference)
                {
                    byte[] to = GetSender(output)/*"AMhdgsKCUzdPn94uFPEwZEEpTdoY8yMqYY".AsByteArray()*/;
                    BigInteger neoValue = 1/*GetBalanceOfCurrency(from, NeoAssetId)*/;
                    BigInteger gasValue = 1/*GetBalanceOfCurrency(from, GasAssetId)*/;
                    Transfer(from, to, neoValue);
                    Transfer(from, to, gasValue);
                    Runtime.Notify(from);
                    Runtime.Notify(to);
                }
            }

            return true;
        }

        public static byte[] GetSender(TransactionOutput output)
        {
            return output.AssetId == NeoAssetId ? output.ScriptHash : new byte[] { };
        }

        /**
         * Insert minimum transaction value and
         * timestamp(Time of expiration of the contract) into the storage
        */
        public static bool Init(ulong txValMin, uint timeStamp)
        {
            if ((txValMin == 0) || (timeStamp == 0))
                return false;
            // Get from the storage minimum transaction value and timestamp
            byte[] curTxValMin = Storage.Get(Storage.CurrentContext, "txValMin");
            byte[] curTimeStamp = Storage.Get(Storage.CurrentContext, "timeStamp");

            if (!((curTxValMin == null) || (curTimeStamp == null)))
                return false;
            // Put minimum transaction value and timestamp into the storage if the value is null
            Storage.Put(Storage.CurrentContext, "txValMin", txValMin);
            Storage.Put(Storage.CurrentContext, "timeStamp", timeStamp);

            return true;
        }

        /**
        * Attempt to deposit assets to the senders account balance
        */
        public static bool DepositAsset()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput reference = tx.GetReferences()[0];

            if (reference.AssetId != NeoAssetId && reference.AssetId != GasAssetId)
            {
                // Transferred asset is not neo or gas, do nothing
                Runtime.Notify("DepositAsset() reference.AssetID is not NEO|GAS", reference.AssetId);
                return false;
            }

            TransactionOutput[] outputs = tx.GetOutputs();
            byte[] sender = reference.ScriptHash; // The sender of funds, balance will be credited here
            byte[] receiver = ExecutionEngine.ExecutingScriptHash; // ScriptHash of SC
            BigInteger receivedNeo = 0;
            BigInteger receivedGas = 0;

            Runtime.Notify("DepositAsset() sender of funds", reference.ScriptHash);
            Runtime.Notify("DepositAsset() recipient of funds", ExecutionEngine.ExecutingScriptHash);

            // Calculate the total amount of NEO|GAS transferred to the smart contract address
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == receiver)
                {
                    // Only add funds to total received value if receiver is the recipient of the output
                    BigInteger receivedValue = (ulong)output.Value / 100000000;
                    BigInteger txValMin = new BigInteger(Storage.Get(Storage.CurrentContext, "txValMin"));
                    // Check receivedValue with minimum transaction value
                    if (receivedValue < txValMin)
                    {
                        return false;
                    }

                    Runtime.Notify("DepositAsset() Received Deposit type", reference.AssetId);
                    if (reference.AssetId == NeoAssetId)
                    {
                        Runtime.Notify("DepositAsset() adding NEO to total", receivedValue);
                        receivedNeo += receivedValue;
                    }
                    else if (reference.AssetId == GasAssetId)
                    {
                        Runtime.Notify("DepositAsset() adding GAS to total", receivedValue);
                        receivedGas += receivedValue;
                    }
                }
            }

            Runtime.Notify("DepositAsset() receivedNEO", receivedNeo);
            Runtime.Notify("DepositAsset() receivedGAS", receivedGas);

            if (receivedNeo > 0)
            {
                CurrencyDeposit(sender, NeoAssetId, receivedNeo);
                TransferAsset(null, sender, NeoAssetId, receivedNeo);
            }

            if (receivedGas > 0)
            {
                CurrencyDeposit(sender, GasAssetId, receivedGas);
                TransferAsset(null, sender, GasAssetId, receivedGas);
            }

            return true;
        }

        /**
         * Funds are released back to the owner of an order when it is cancelled
         */
        public static void CurrencyDeposit(byte[] address, byte[] currency, BigInteger newFunds)
        {
            byte[] indexName = GetCurrencyIndexName(address, currency);
            BigInteger currentBalance = GetBalanceOfCurrency(address, currency);
            Runtime.Notify("CurrencyDeposit() indexName", indexName);
            Runtime.Notify("CurrencyDeposit() currentBalance", currentBalance);
            Runtime.Notify("CurrencyDeposit() newFunds", newFunds);

            BigInteger updateBalance = currentBalance + newFunds;

            if (updateBalance <= 0)
            {
                Runtime.Notify("CurrencyDeposit() removing balance reference", updateBalance);
                Storage.Delete(Storage.CurrentContext, indexName);
            }
            else
            {
                Runtime.Notify("CurrencyDeposit() setting balance", updateBalance);
                Storage.Put(Storage.CurrentContext, indexName, updateBalance);
            }
        }

        /**
        * Retrieve an indexName comprised of address+currency
        */
        public static byte[] GetCurrencyIndexName(byte[] address, byte[] currency)
        {
            return address.Concat(currency);
        }

        /**
        * Retrieve the currency balance for address
        * <param name="address">address to check balance for</param>
        * <param name="currency">currency type</param>
        */
        public static BigInteger GetBalanceOfCurrency(byte[] address, byte[] currency)
        {
            byte[] indexName = GetCurrencyIndexName(address, currency);
            Runtime.Notify("GetBalanceOfCurrency() indexName", indexName);

            BigInteger currentBalance = Storage.Get(Storage.CurrentContext, indexName).AsBigInteger();
            Runtime.Notify("GetBalanceOfCurrency() currency", currency);
            Runtime.Notify("GetBalanceOfCurrency() currentBalance", currentBalance);
            return currentBalance;
        }

        /**
        * Transfer value between from and to accounts
        */
        public static bool Transfer(byte[] from, byte[] to, BigInteger transferValue)
        {
            Runtime.Notify("Transfer() transferValue", transferValue);
            if (transferValue <= 0)
            {
                // Don't accept stupid values
                Runtime.Notify("Transfer() transferValue was <= 0", transferValue);
                return false;
            }

            if (from == to)
            {
                // Don't waste resources when from==to
                Runtime.Notify("Transfer() from == to failed", to);
                return true;
            }

            BigInteger fromBalance = GetBalanceOfCurrency(from, NeoAssetId); // Retrieve balance 
            if (fromBalance < transferValue)
            {
                Runtime.Notify("Transfer() fromBalance < transferValue", fromBalance);
                // Don't transfer if funds not available
                return false;
            }

            SetBalanceOf(from, fromBalance - transferValue); // Remove balance from originating account
            SetBalanceOf(to, BalanceOf(to) + transferValue); // Set new balance for destination account

            Transferred(from, to, transferValue);
            return true;
        }

        /**
        * Set newBalance for address
        */
        private static void SetBalanceOf(byte[] address, BigInteger newBalance)
        {
            if (newBalance <= 0)
            {
                Runtime.Notify("SetBalanceOf() removing balance reference", newBalance);
                Storage.Delete(Storage.CurrentContext, address);
            }
            else
            {
                Runtime.Notify("SetBalanceOf() setting balance", newBalance);
                Storage.Put(Storage.CurrentContext, address, newBalance);
            }
        }

        /**
        * Retrieve the number of tokens stored in address
        */
        public static BigInteger BalanceOf(byte[] address)
        {
            BigInteger currentBalance = Storage.Get(Storage.CurrentContext, address).AsBigInteger();
            Runtime.Notify("BalanceOf() currentBalance", currentBalance);
            return currentBalance;
        }
    }
}