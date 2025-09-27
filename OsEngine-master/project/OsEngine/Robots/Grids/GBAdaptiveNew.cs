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
    [Bot("GBAdaptiveNew")]
    public class GBAdaptiveNew : BotPanel
    {
        private StrategyParameterString _regime;
        private StrategyParameterString _tradeAssetInPortfolio;

        // Indicator settings
        private StrategyParameterInt _bollingerLength;
        private StrategyParameterDecimal _bollingerDeviation;

        private StrategyParameterInt _linesCount;
        private StrategyParameterDecimal _linesStep;
        private StrategyParameterDecimal _profitValue;

        private Aindicator _bollinger;
        private Aindicator _volatilityStages;

        private BotTabSimple _tab;

        // Volatility settings
        private StrategyParameterInt _volatilitySlowSmaLength;
        private StrategyParameterInt _volatilityFastSmaLength;
        private StrategyParameterDecimal _volatilityChannelDeviation;
        private StrategyParameterDecimal _lowVolatilityLots;
        private StrategyParameterDecimal _middleVolatilityLots;
        private StrategyParameterDecimal _normalVolatilityLots;
        private StrategyParameterDecimal _highVolatilityLots;



        public GBAdaptiveNew(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            _tab.Connector.TestStartEvent += Connector_TestStartEvent;

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            _bollingerLength = CreateParameter("Bollinger length", 21, 7, 48, 1, "Base");
            _bollingerDeviation = CreateParameter("Bollinger deviation", 1.0m, 1, 5, 0.1m, "Base");

            _linesCount = CreateParameter("Grid lines count", 10, 10, 300, 10, "Grid");
            _linesStep = CreateParameter("Grid lines step", 25.0m, 1, 2, 0.1m, "Grid");
            _profitValue = CreateParameter("Profit", 25.0m, 1, 2, 0.1m, "Grid");
            
            _tradeAssetInPortfolio = CreateParameter("Asset in portfolio", "Prime", "Grid");

            // Volatility settings
            _volatilitySlowSmaLength = CreateParameter("Volatility slow sma length", 25, 10, 80, 3, "Volatility filter");
            _volatilityFastSmaLength = CreateParameter("Volatility fast sma length", 7, 10, 80, 3, "Volatility filter");
            _volatilityChannelDeviation = CreateParameter("Volatility channel deviation", 0.5m, 1.0m, 50, 4, "Volatility filter");

            _lowVolatilityLots = CreateParameter("Low Volatitity Lots", 10m, 1m, 10m, 1m, "Volatility filter");
            _middleVolatilityLots = CreateParameter("Middle Volatitity Lots", 6m, 1m, 10m, 1m, "Volatility filter");
            _normalVolatilityLots = CreateParameter("Normal Volatitity Lots", 3m, 1m, 10m, 1m, "Volatility filter");
            _highVolatilityLots = CreateParameter("High Volatitity Lots", 1m, 1m, 10m, 1m, "Volatility filter");

            // Create indicator Bollinger
            _bollinger = IndicatorsFactory.CreateIndicatorByName("Bollinger", name + "Bollinger", false);
            _bollinger = (Aindicator)_tab.CreateCandleIndicator(_bollinger, "Prime");
            ((IndicatorParameterInt)_bollinger.Parameters[0]).ValueInt = _bollingerLength.ValueInt;
            ((IndicatorParameterDecimal)_bollinger.Parameters[1]).ValueDecimal = _bollingerDeviation.ValueDecimal;
            _bollinger.Save();

            // Create indicator VolatilityStages
            _volatilityStages = IndicatorsFactory.CreateIndicatorByName("VolatilityStagesAW", name + "VolatilityStages", false);
            _volatilityStages = (Aindicator)_tab.CreateCandleIndicator(_volatilityStages, "VolatilityStagesArea");
            _volatilityStages.ParametersDigit[0].Value = _volatilitySlowSmaLength.ValueInt;
            _volatilityStages.ParametersDigit[1].Value = _volatilityFastSmaLength.ValueInt;
            _volatilityStages.ParametersDigit[2].Value = _volatilityChannelDeviation.ValueDecimal;
            _volatilityStages.Save();

            ParametrsChangeByUser += ParametersChangeByUser;

            Description = OsLocalization.Description.DescriptionLabel34;
        }


        private void ParametersChangeByUser()
        {
            if (_bollingerLength.ValueInt != _bollinger.ParametersDigit[0].Value ||
                _bollingerLength.ValueInt != _bollinger.ParametersDigit[1].Value)
            {
                _bollinger.ParametersDigit[0].Value = _bollingerLength.ValueInt;
                _bollinger.ParametersDigit[1].Value = _bollingerDeviation.ValueDecimal;
                _bollinger.Reload();
            }

            if (_volatilityStages.ParametersDigit[0].Value != _volatilitySlowSmaLength.ValueInt
                || _volatilityStages.ParametersDigit[1].Value != _volatilityFastSmaLength.ValueInt
                || _volatilityStages.ParametersDigit[2].Value != _volatilityChannelDeviation.ValueDecimal)
            {
                _volatilityStages.ParametersDigit[0].Value = _volatilitySlowSmaLength.ValueInt;
                _volatilityStages.ParametersDigit[1].Value = _volatilityFastSmaLength.ValueInt;
                _volatilityStages.ParametersDigit[2].Value = _volatilityChannelDeviation.ValueDecimal;
                _volatilityStages.Reload();
            }
        }

        public override string GetNameStrategyType()
        {
            return "GBAdaptiveNew";
        }

        public override void ShowIndividualSettingsDialog()
        {

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

                decimal stage = _volatilityStages.DataSeries[0].Values[_volatilityStages.DataSeries[0].Values.Count - 2];
                if (stage == 1)
                {
                    grid.GridCreator.StartVolume = _lowVolatilityLots.ValueDecimal;
                }
                else if (stage == 2)
                {
                    grid.GridCreator.StartVolume = _middleVolatilityLots.ValueDecimal;
                }
                else if (stage == 3)
                {
                    grid.GridCreator.StartVolume = _normalVolatilityLots.ValueDecimal;
                }
                else if (stage == 4)
                {
                    grid.GridCreator.StartVolume = _highVolatilityLots.ValueDecimal;
                }

                grid.GridCreator.TradeAssetInPortfolio = _tradeAssetInPortfolio.ValueString;
                grid.GridCreator.TypeVolume = TradeGridVolumeType.Contracts;
                grid.GridCreator.FirstPrice = lastPrice;
                grid.GridCreator.LineCountStart = _linesCount.ValueInt;
                grid.GridCreator.LineStep = _linesStep.ValueDecimal;
                grid.GridCreator.TypeStep = TradeGridValueType.Absolute;

                grid.GridCreator.TypeProfit = TradeGridValueType.Absolute;
                grid.GridCreator.ProfitStep = _profitValue.ValueDecimal;

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
