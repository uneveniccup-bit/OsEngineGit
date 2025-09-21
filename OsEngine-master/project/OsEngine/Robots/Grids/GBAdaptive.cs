/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Language;
using OsEngine.OsTrader.Grids;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

/* Description
A robot demonstrating grid operation in countertrend.
Throws out a grid of "MarketMaking" type
Bollinger indicator serves as a signal for grid throwing.
When the candlestick closing price is above the upper channel line of the indicator - the grid is thrown into the SHORT.
When the candlestick closing price is below the lower line of the indicator channel - the grid is thrown to LONG.
 */

namespace OsEngine.Robots.Grids
{
    [Bot("GBAdaptive")]
    public class GBAdaptive : BotPanel
    {
        private StrategyParameterString _regime;

        private StrategyParameterString _volumeType;
        private StrategyParameterDecimal _volume;
        private StrategyParameterString _tradeAssetInPortfolio;

        private StrategyParameterInt _bollingerLength;
        private StrategyParameterDecimal _bollingerDeviation;
        private StrategyParameterDecimal _procentOfDayVolatility;

        private StrategyParameterInt _linesCount;
        private decimal _linesStep;
        private decimal _profitValue;

        // Volatility settings
        private StrategyParameterInt _daysVolatilityAdaptive;

        private Aindicator _bollinger;

        private BotTabSimple _tab;
        private StrategyParameterDecimal volaSma;

        public GBAdaptive(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            _tab.Connector.TestStartEvent += Connector_TestStartEvent;

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            _bollingerLength = CreateParameter("Bollinger length", 21, 7, 48, 1, "Base");
            _bollingerDeviation = CreateParameter("Bollinger deviation", 1.0m, 1, 5, 0.1m, "Base");

            _linesCount = CreateParameter("Grid lines count", 10, 10, 300, 10, "Grid");
            _volumeType = CreateParameter("Volume type", "Contracts", new[] { "Contracts", "Contract currency", "Deposit percent" }, "Grid");
            _volume = CreateParameter("Volume on one line", 1, 1.0m, 50, 4, "Grid");
            _tradeAssetInPortfolio = CreateParameter("Asset in portfolio", "Prime", "Grid");
            

            // Volatility settings
            _daysVolatilityAdaptive = CreateParameter("Days volatility adaptive", 1, 0, 20, 1);
            _procentOfDayVolatility = CreateParameter("Procent Volatility to Step", 50m, 0m, 20m, 1m);

            // Create indicator Bollinger
            _bollinger = IndicatorsFactory.CreateIndicatorByName("Bollinger", name + "Bollinger", false);
            _bollinger = (Aindicator)_tab.CreateCandleIndicator(_bollinger, "Prime");
            ((IndicatorParameterInt)_bollinger.Parameters[0]).ValueInt = _bollingerLength.ValueInt;
            ((IndicatorParameterDecimal)_bollinger.Parameters[1]).ValueDecimal = _bollingerDeviation.ValueDecimal;
            _bollinger.Save();

            ParametrsChangeByUser += ParametersChangeByUser;

            Description = OsLocalization.Description.DescriptionLabel34;
        }


        private void ParametersChangeByUser()
        {
            ((IndicatorParameterInt)_bollinger.Parameters[0]).ValueInt = _bollingerLength.ValueInt;
            ((IndicatorParameterDecimal)_bollinger.Parameters[1]).ValueDecimal = _bollingerDeviation.ValueDecimal;
            _bollinger.Save();
            _bollinger.Reload();
        }

        public override string GetNameStrategyType()
        {
            return "GBAdaptive";
        }

        public override void ShowIndividualSettingsDialog()
        {

        }



        // Volatility adaptation
        private void AdaptSignalCandleHeight(List<Candle> candles)
        {
            if (_daysVolatilityAdaptive.ValueInt <= 0)
            {
                return;
            }

            // 1 we calculate the movement from high to low within N days

            decimal minValueInDay = decimal.MaxValue;
            decimal maxValueInDay = decimal.MinValue;

            List<decimal> volaInDays = new List<decimal>();

            DateTime date = candles[candles.Count - 1].TimeStart.Date;

            int days = 0;

            for (int i = candles.Count - 1; i >= 0; i--)
            {
                Candle curCandle = candles[i];

                if (curCandle.TimeStart.Date < date)
                {
                    date = curCandle.TimeStart.Date;
                    days++;

                    decimal volaAbsToday = maxValueInDay - minValueInDay;

                    volaInDays.Add(volaAbsToday);

                    minValueInDay = decimal.MaxValue;
                    maxValueInDay = decimal.MinValue;
                }

                if (days >= _daysVolatilityAdaptive.ValueInt)
                {
                    break;
                }

                if (curCandle.High > maxValueInDay)
                {
                    maxValueInDay = curCandle.High;
                }

                if (curCandle.Low < minValueInDay)
                {
                    minValueInDay = curCandle.Low;
                }

                if (i == 0)
                {
                    days++;

                    decimal volaAbsToday = maxValueInDay - minValueInDay;
                    volaInDays.Add(volaAbsToday);
                }
            }

            if (volaInDays.Count == 0)
            {
                return;
            }

            // 2 we average this movement. We need average volatility

            decimal volaSma = 0;

            for (int i = 0; i < volaInDays.Count; i++)
            {
                volaSma += volaInDays[i];
            }

            volaSma = volaSma / volaInDays.Count;
            _linesStep = volaSma;
            _profitValue = volaSma;
        }



        private void Connector_TestStartEvent()
        {
            if (_tab.GridsMaster == null)
            {
                return;
            }

            for (int i = 0; i < _tab.GridsMaster.TradeGrids.Count; i++)
            {
                TradeGrid grid = _tab.GridsMaster.TradeGrids[i];
                _tab.GridsMaster.DeleteAtNum(grid.Number);
                i--;
            }
        }

        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (_regime.ValueString == "Off")
            {
                return;
            }

            if (candles.Count < _bollingerLength.ValueInt)
            {
                return;
            }

            if (candles.Count > 20 &&
              candles[candles.Count - 1].TimeStart.Date != candles[candles.Count - 2].TimeStart.Date)
            {
                AdaptSignalCandleHeight(candles);
            }


            if (_tab.GridsMaster.TradeGrids.Count != 0)
            {
                LogicCloseGrid(candles);
            }

            if (_tab.GridsMaster.TradeGrids.Count == 0)
            {
                LogicCreateGrid(candles);
            }

        }

        private void LogicCreateGrid(List<Candle> candles)
        {
            decimal lastUpLine = _bollinger.DataSeries[0].Last;
            decimal lastDownLine = _bollinger.DataSeries[1].Last;

            if (lastUpLine == 0
                || lastDownLine == 0)
            {
                return;
            }

            decimal lastPrice = candles[^1].Close;

            if (lastPrice > lastUpLine
                || lastPrice < lastDownLine)
            {
                TradeGrid grid = _tab.GridsMaster.CreateNewTradeGrid();

                grid.GridType = TradeGridPrimeType.MarketMaking;

                grid.GridCreator.StartVolume = _volume.ValueDecimal;
                grid.GridCreator.TradeAssetInPortfolio = _tradeAssetInPortfolio.ValueString;

                if (_volumeType.ValueString == "Contracts")
                {
                    grid.GridCreator.TypeVolume = TradeGridVolumeType.Contracts;
                }
                else if (_volumeType.ValueString == "Contract currency")
                {
                    grid.GridCreator.TypeVolume = TradeGridVolumeType.ContractCurrency;
                }
                else if (_volumeType.ValueString == "Deposit percent")
                {
                    grid.GridCreator.TypeVolume = TradeGridVolumeType.DepositPercent;
                }

                grid.GridCreator.FirstPrice = lastPrice;
                grid.GridCreator.LineCountStart = _linesCount.ValueInt;
                grid.GridCreator.LineStep = (_linesStep/100) * _procentOfDayVolatility;
                grid.GridCreator.TypeStep = TradeGridValueType.Absolute;

                grid.GridCreator.TypeProfit = TradeGridValueType.Absolute;
                grid.GridCreator.ProfitStep = (_profitValue/100) * _procentOfDayVolatility;

                if (lastPrice > lastUpLine)
                {
                    grid.GridCreator.GridSide = Side.Sell;
                }
                else if (lastPrice < lastDownLine)
                {
                    grid.GridCreator.GridSide = Side.Buy;
                }
                grid.GridCreator.CreateNewGrid(_tab, TradeGridPrimeType.MarketMaking);

                grid.Save();

                grid.Regime = TradeGridRegime.On;
            }
        }

        private void LogicCloseGrid(List<Candle> candles)
        {
            TradeGrid grid = _tab.GridsMaster.TradeGrids[0];

            // 1 проверяем сетку на то что она уже прекратила работать и её надо удалить

            if (grid.HaveOpenPositionsByGrid == false
                && grid.Regime == TradeGridRegime.Off)
            { // Grid is stop work
                _tab.GridsMaster.DeleteAtNum(grid.Number);
                return;
            }

            if (grid.Regime == TradeGridRegime.CloseForced)
            {
                return;
            }

            // 2 проверяем сетку на обратную сторону канала. Может пора её закрывать

            decimal lastUpLine = _bollinger.DataSeries[0].Last;
            decimal lastDownLine = _bollinger.DataSeries[1].Last;

            if (lastUpLine == 0
                || lastDownLine == 0)
            {
                return;
            }

            decimal lastPrice = candles[^1].Close;

            Side gridSide = grid.GridCreator.GridSide;

            if (gridSide == Side.Buy
                && lastPrice > lastUpLine)
            {
                grid.Regime = TradeGridRegime.CloseForced;
            }
            else if (gridSide == Side.Sell
                && lastPrice < lastDownLine)
            {
                grid.Regime = TradeGridRegime.CloseForced;
            }
        }
    }
}
